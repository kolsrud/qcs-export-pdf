using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Qlik.Sense.RestClient;

namespace QcsExportPdf
{
	internal class Arguments
	{
		public Uri Uri { get; set; }
		public string ApiKey { get; set; }
		public string AppId { get; set;  }
		public string ObjectId { get; set; }
		public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
	}

	internal class Program
	{
		static void Main(string[] args)
		{
			var arguments = ProcessArguments(args);

			var client = new RestClient(arguments.Uri);
			client.AsApiKeyViaQcs(arguments.ApiKey);

			var requestBody = CreateRequestBody(arguments.AppId, arguments.ObjectId);

			RunMainLoop(client, requestBody, arguments.Interval);
		}

		private static void RunMainLoop(RestClient client, JToken requestBody, TimeSpan argumentsInterval)
		{
			int i = 0;
			var nextRun = DateTime.MinValue;
			while (true)
			{
				if (nextRun == DateTime.MinValue)
				{
					nextRun = DateTime.Now + argumentsInterval;
				}
				else
				{
					while (DateTime.Now > nextRun)
					{
						nextRun += argumentsInterval;
					}
				}

				var thisRun = DateTime.Now;
				Console.Write($"{i}\t{nextRun} - Generating pdf... ");
				var httpRsp = client.PostHttpAsync("/api/v1/reports", requestBody);
				var httpResult = httpRsp.Result;
				var statusLocation = httpResult.Headers.Location;
				var dataLocation = AwaitExportCompletion(client, statusLocation.AbsolutePath);

				var outputFile = $"generated_report_{i}_{thisRun:yyyyMMddTHHmmss}.pdf";
				Console.Write($"Downloading file... ");
				var dataLocationUri = new Uri(dataLocation);
				var bytes = client.GetBytes(dataLocationUri.AbsolutePath);
				Console.Write("Done! ");

				File.WriteAllBytes(outputFile, bytes);
				Console.WriteLine($"Wrote {bytes.Length} to file: {outputFile}");
				i++;
				Thread.Sleep(nextRun - DateTime.Now);
			}
		}

		private static Arguments ProcessArguments(string[] args)
		{
			if (args.Length == 0 || args.Any("-h".Equals))
			{
				PrintUsage();
			}
			var arguments = new Arguments();
			int i = 0;
			while (i < args.Length)
			{
				if (i + 1 == args.Length)
				{
					PrintUsage($"Error: Missing argument to flag \"{args[i]}\"");
				}
				switch (args[i])
				{
					case "-url":
						try
						{
							arguments.Uri = new Uri(args[i + 1]);
						}
						catch (Exception e)
						{
							PrintUsage($"Unable to parse url: {args[i+1]}");
						};
						break;
					case "-apiKey":
						arguments.ApiKey = args[i+1];
						break;
					case "-appId":
						arguments.AppId = args[i + 1];
						break;
					case "-objId":
						arguments.ObjectId = args[i + 1];
						break;
					case "-t":
						if (int.TryParse(args[i + 1], out var n))
						{
							arguments.Interval = TimeSpan.FromSeconds(n);
						}
						else
						{
							PrintUsage($"Unable to parse interval as integer: {args[i+1]}");
						}
						break;
				}
				i += 2;
			}

			return arguments;
		}

		private static void PrintUsage(string errorMessage = null)
		{
			if (errorMessage != null)
			{
				Console.WriteLine(errorMessage);
			}

			Console.WriteLine("Usage:   QcsExportPdf -url <url> -apiKey <apiKey> -appId <appId> -objId <objId> [-t <seconds>] [-h]");
			Console.WriteLine("         QcsExportPdf [-h]");
			Console.WriteLine("Example: QcsExportPdf -url https://mytenant.eu.qlikclouod.com -appId e90a34b7-810a-4012-b394-20fd9ce5cd4f -objId -apiKey eyJhb... -t 60");
			Console.WriteLine("         QcsExportPdf -h");
			Console.WriteLine("Arguments:");
			Console.WriteLine("  url    : Url to the Qlik Cloud tenant.");
			Console.WriteLine("  apiKey : Api key generated for the Qlik Cloud tenant.");
			Console.WriteLine("  appId  : The identifier of the app to connect to.");
			Console.WriteLine("  objId  : The identifier of the object for which to export a pdf.");
			Console.WriteLine("  t      : Time interval in seconds between renderings. (Default: 60)");
			Console.WriteLine("  h      : Print this message.");
			Environment.Exit(0);
		}

		private static string AwaitExportCompletion(IRestClient client, string statusLocation)
		{
			var rsp = client.Get<JToken>(statusLocation);
			while (rsp["status"].Value<string>() != "done")
			{
				Thread.Sleep(TimeSpan.FromSeconds(1));
				rsp = client.Get<JToken>(statusLocation);
			}

			return rsp["results"][0]["location"].Value<string>();
		}

		private static JToken CreateRequestBody(string appId, string objectId)
		{
			var body = new
			{
				type = "sense-image-1.0",
				output = new
				{
					outputId = "Chart_pdf",
					type = "pdf",
					pdfOutput = new { }
				},
				senseImageTemplate = new
				{
					appId = appId,
					visualization = new
					{
						id = objectId,
						widthPx = 613,
						heightPx = 409,
					}
				},
			};
			return JToken.FromObject(body);
		}
	}
}
