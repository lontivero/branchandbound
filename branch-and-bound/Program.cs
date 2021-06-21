using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace branch_and_bound
{
	class Program
	{
		private static readonly Random Random = new();

		static void Main(string[] args)
		{
			var minWalletSize = GetOptions(args, "--min-wallet-size", 0);
			var maxWalletSize = GetOptions(args, "--max-wallet-size", 100);
			var incWalletSize = GetOptions(args, "--inc-wallet-size", 1);
			var minTolerance = GetOptions(args, "--min-tolerance", 0);
			var maxTolerance = GetOptions(args, "--max-tolerance", 100_000);
			var incTolerance = GetOptions(args, "--inc-tolerance", 1_000);
			var runs = GetOptions(args, "--runs", 100);
			var denoms = GetOptions(args, "--random-denoms", 0) == 1 ? "rnd" : "std";
			var plot = GetOptions(args, "--plot", 1) == 1;
			var image = GetOptions(args, "--output", "bnb-output.png");

			using var writer = Console.Out;
			if (plot)
			{
				writer.WriteLine("$data << EOD");
			}
			for (var walletSize = minWalletSize; walletSize <= maxWalletSize; walletSize += incWalletSize)
			{
				var wallet = GenerateRandomWallet(walletSize, denoms);
				var walletBalance = wallet.Sum();

				for (var tolerance = minTolerance; tolerance <= maxTolerance; tolerance += incTolerance)
				{
					var successes = 0;
					for (var attempt = 0; attempt < runs; attempt++)
					{
						var paymentAmount = (ulong)(Random.NextDouble() * walletBalance);
						if (Bnb.TrySolve(wallet, paymentAmount, (ulong)tolerance, out var _))
						{
							successes++;
						}
					}

					writer.Write($"{(float)successes / runs:0.###}\t");
				}
				writer.WriteLine();
			}
			if (plot)
			{
				writer.WriteLine("EOD");
				writer.WriteLine();
				writer.WriteLine("set title 'Building Txs with no change (success rate)'");
				writer.WriteLine("unset key");
				writer.WriteLine("set zlabel \"Sucess\\nrate\"");
				writer.WriteLine("set xlabel 'Tolerance'");
				writer.WriteLine("set ylabel 'Wallet size'");
				writer.WriteLine("set surface");
				writer.WriteLine("set hidden3d");
				var term = Path.GetExtension(image) switch
				{
					".png" => "png",
					".ps" => "ps",
					_ => "png"
				};
				writer.WriteLine($"set term {term}");
				writer.WriteLine($"set output '{image}'");
				writer.WriteLine($"splot $data matrix using ({minTolerance}+$1*{incTolerance}):({minWalletSize}+$2*{incWalletSize}):3 w lines");
			}
		}

		private static ulong[] GenerateRandomWallet(int size, string denomsGenerator)
		{
			var denoms = StandardDenomination.Values;
			Func<ulong> generator = denomsGenerator switch
			{
				"rnd" => () => (ulong)Random.Next((int)Money.Coins(0.0000090m), (int)Money.Coins(10)),
				"std" => () => denoms[Random.Next(denoms.Length - 1)],
				//"std" => () => denoms[Math.Min((int)Random.LogNormal(3.4, 0.4), denoms.Length - 1)],
				_ => throw new NotSupportedException()
			};
			return Enumerable.Range(1, size)
				.Select(_ => generator())
				.OrderByDescending(x => x)
				.ToArray();
		}

		private static string? GetOptions(string[] args, string argName)
		{
			var opt = args.SkipWhile(x => x != argName);
			var present = opt.Any();
			if (!present)
			{
				return null;
			}
			else
			{
				var values = opt.Skip(1).TakeWhile(x => !x.StartsWith("--"));
				if (!values.Any())
				{
					throw new NotSupportedException($"command line option `{argName}` has not value.");
				}
				return values.First();
			}
		}
		private static string GetOptions(string[] args, string argName, string defaultValue) =>
			(GetOptions(args, argName) is { } value) ? value : defaultValue;

		private static int GetOptions(string[] args, string argName, int defaultValue) =>
			(GetOptions(args, argName) is { } value) ? int.Parse(value) : defaultValue;
	}

	public static class Bnb
	{
		public static bool TrySolve(ulong[] wallet, ulong target, ulong tolerance, out ulong[] solution)
		{
			try
			{
				solution = SolveX(wallet, target, tolerance).ToArray();
				return solution.Any();
			}
			catch (InvalidOperationException)
			{
				solution = Array.Empty<ulong>();
				return false;
			}
		}

		private static ulong[] SolveX(ulong[] set, ulong target, ulong tolerance)
		{
			var coins = new Stack<ulong>(set.Reverse());
			var selection = new Stack<ulong>();

			while (true)
			{
				var totalSelected = selection.Sum();

				if (totalSelected < target && coins.Any())
				{
					selection.Push(coins.Pop());
				}
				else if (totalSelected >= target && totalSelected <= target + tolerance)
				{
					return selection.ToArray();
				}
				else if (totalSelected > target + tolerance && selection.Any())
				{
					selection.Pop();
				}
				else
				{
					throw new InvalidOperationException($"It was not possible to find a set of coins to meet the required {target} amount.");
				}
			}
		}
	}

	public static class StandardDenomination
	{
		private static readonly ulong MaxAmount = Money.Coins(8);
		private static readonly ulong Dust = Money.Coins(0.0000090m);
		public static readonly ImmutableArray<ulong> Values = Generate().ToImmutableArray();

		private static IEnumerable<ulong> Multiple(IEnumerable<ulong> coefficients, IEnumerable<ulong> values) =>
			values
			.SelectMany(c => coefficients, (v, c) => c * v)
			.TakeWhile(x => x <= MaxAmount);

		private static IEnumerable<ulong> PowersOf(int b) =>
			Enumerable
			.Range(0, int.MaxValue)
			.Select(x => (ulong)Math.Pow(b, x))
			.TakeWhile(x => x <= MaxAmount);

		private static IEnumerable<ulong> Generate()
		{
			var ternary = Multiple(new ulong[] { 1, 2 }, PowersOf(3));
			var preferredValueSeries = Multiple(new ulong[] { 1, 2, 5 }, PowersOf(10));

			return PowersOf(2)
				.Union(ternary)
				.Union(preferredValueSeries)
				.Where(x => x > Dust)
				.OrderBy(v => v);
		}
	}

	public static class RandomExtensions
	{
		public static double LogNormal(this Random random)
			=> random.LogNormal(1, 0.5);

		public static double LogNormal(this Random random, double mean, double stddev)
			=> Math.Exp(random.Gaussian(mean, stddev));

		public static double Gaussian(this Random random, double mean, double stddev)
		{
			var x1 = 1 - random.NextDouble();
			var x2 = 1 - random.NextDouble();

			var y1 = Math.Sqrt(-2.0 * Math.Log(x1)) * Math.Cos(2.0 * Math.PI * x2);
			return y1 * stddev + mean;
		}
	}

	public static class Money
	{
		public static ulong Coins(decimal coins) => (ulong)(coins * 100_000_000);
	}

	public static class LinqExtensions
	{
		public static ulong Sum(this IEnumerable<ulong> me) =>
			me.Aggregate(0ul, (x, y) => x + y);
	}
}
