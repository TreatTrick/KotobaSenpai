using System.Text;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Platform.Windows.Llm;
using KotobaSenpai.Platform.Windows.Settings;

namespace KotobaSenpai.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("KotobaSenpai API configuration\nStart with: capabilities --json\nCommands: llm models|detect|configure --stdin --json; llm status --json\nProvide one UTF-8 JSON object through stdin. No key is required for capabilities or status.");
            return 0;
        }
        var command = args switch
        {
            ["capabilities", "--json"] => "capabilities",
            ["llm", "status", "--json"] => "status",
            ["llm", "models" or "detect" or "configure", "--stdin", "--json"] => args[1],
            _ => "",
        };
        if (command.Length == 0)
            return Write(AgentApiResult.Failure("invalid_command", 2) with { NextAction = "Run capabilities --json or --help." });
        if (command == "capabilities")
        {
            Console.WriteLine(AgentCommandProtocol.Serialize(AgentCommandProtocol.Capabilities(
                AppContext.BaseDirectory, AgentApiContract.ProtocolOrder)));
            return 0;
        }
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var request = new AgentApiRequest { SchemaVersion = AgentApiContract.SchemaVersion };
        if (command != "status")
        {
            using var inputTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            inputTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var parsed = await AgentCommandProtocol.ReadAsync(Console.In, command, inputTimeout.Token);
            if (parsed.Error is not null || parsed.Request is null)
                return Write(parsed.Error ?? AgentApiResult.Failure("invalid_json", 2));
            request = parsed.Request;
        }
        try
        {
            var runner = new ConfigurationProcessRunner(Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.App.exe"));
            var result = await runner.ExecuteAsync(command, request,
                token => ExecuteCommandAsync(command, request, token), cancellation.Token);
            return Write(result, request.ApiKey?.Trim());
        }
        catch (Exception)
        {
            return Write(AgentApiResult.Failure("cli_failed", 5) with { Applied = null, NextAction = "Query status before retrying." });
        }
    }

    private static async Task<AgentApiResult> ExecuteCommandAsync(string command, AgentApiRequest request, CancellationToken cancellationToken)
    {
        var settings = new SettingsService(SettingsService.DefaultFilePath, readOnly: command != "configure");
        using var http = LlmHttpClient.Create();
        var registry = new LlmProtocolRegistry();
        var analyzer = new LlmPhraseAnalyzer(settings, http, protocolRegistry: registry);
        var service = new LlmConfigurationService(settings,
            new LlmConnectionDetector(analyzer, registry), new HttpLlmModelCatalog(http, registry));
        return await service.ExecuteAsync(command, request, cancellationToken);
    }

    private static int Write(AgentApiResult result, string? secret = null)
    {
        Console.WriteLine(AgentCommandProtocol.Serialize(result, secret));
        return result.ExitCode;
    }
}
