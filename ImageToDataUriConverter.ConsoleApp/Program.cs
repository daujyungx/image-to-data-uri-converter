using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ImageToDataUriConverter.ConsoleApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await CreateRootCommand().InvokeAsync(args);
    }

    private static RootCommand CreateRootCommand()
    {
        var convertCommand = new RootCommand("Image to Data URI converter.");
        Command htmlCommand = new("html", "convert src attribute of img elements in html.");
        Option<string> htmlInputOption = new("--input", "input html file path or url.") { IsRequired = true, };
        htmlInputOption.AddAlias("-i");
        htmlCommand.AddOption(htmlInputOption);
        Option<string> htmlOutputOption = new("--output", "output html file path.") { IsRequired = false, };
        htmlOutputOption.AddAlias("-o");
        htmlCommand.AddOption(htmlOutputOption);
        htmlCommand.SetHandler(async (context) =>
        {
            var htmlInputOptionValue = context.ParseResult.GetValueForOption(htmlInputOption)!;
            var htmlOutputOptionValue = context.ParseResult.GetValueForOption(htmlOutputOption);
            var logger = GetValueForBinder(new LoggerBinder(), context);
            var token = context.GetCancellationToken();
            try
            {
                await ConvertHtml(htmlInputOptionValue, htmlOutputOptionValue, logger, token);
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, ex.Message);
                context.ExitCode = 1;
            }
        });
        convertCommand.AddCommand(htmlCommand);
        Command imageCommand = new("image", "convert image.");
        Option<string> imageInputOption = new("--input", "input image file path or url.") { IsRequired = true, };
        imageInputOption.AddAlias("-o");
        imageCommand.AddOption(imageInputOption);
        imageCommand.SetHandler(async (context) =>
        {
            var imageInputOptionValue = context.ParseResult.GetValueForOption(imageInputOption);
            var logger = GetValueForBinder(new LoggerBinder(), context);
            var token = context.GetCancellationToken();
            try
            {
                await ConvertImage(imageInputOptionValue!, logger, token);
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, ex.Message);
                context.ExitCode = 1;
            }
        });
        convertCommand.AddCommand(imageCommand);
        return convertCommand;
    }

    private static T GetValueForBinder<T>(BinderBase<T> binder, InvocationContext context)
    {
        IValueSource valueSource = binder;
        if (valueSource.TryGetValue(binder, context.BindingContext, out var boundValue) && boundValue is T value)
        {
            return value;
        }
        throw new ApplicationException("fail to get value for binder.");
    }

    private static async Task ConvertHtml(string htmlInputOptionValue, string? htmlOutputOptionValue, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation($"input: {{{nameof(htmlInputOptionValue)}}}", htmlInputOptionValue);

        using var httpClient = new HttpClient();

        var (outputHtml, title) = await Converter.ConvertHtml(htmlInputOptionValue, logger, httpClient, cancellationToken);

        var htmlOutputPath = !string.IsNullOrEmpty(htmlOutputOptionValue)
            ? htmlOutputOptionValue!
            : Path.GetFullPath($"{DateTimeOffset.Now.ToString("yyyy'-'MM'-'dd'T'HHmmssfffffffK").Replace(":", "")} {title}.html");

        await File.WriteAllTextAsync(htmlOutputPath, outputHtml, cancellationToken);
        logger.LogInformation($"output: {{{nameof(htmlOutputPath)}}}", htmlOutputPath);
    }

    private static async Task ConvertImage(string imageInputOptionValue, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation($"input: {{{nameof(imageInputOptionValue)}}}", imageInputOptionValue);

        using var httpClient = new HttpClient();

        var dataUri = await Converter.ToDataUri(new Uri(imageInputOptionValue), httpClient, cancellationToken);

        logger.LogInformation("output:");
        Console.WriteLine(dataUri);
    }
}

internal class LoggerBinder : BinderBase<ILogger>
{
    protected override ILogger GetBoundValue(BindingContext bindingContext) => LoggerFactory
        .Create(builder => builder.AddConsole())
        .CreateLogger("LoggerCategory");
}

