using AngleSharp.Common;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ImageToDataUriConverter;

public static class Converter
{
    public static async Task<string> ToDataUri(Uri uri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var bytes = await GetBytes(uri, httpClient, cancellationToken);
        var extension = Path.GetExtension(uri.LocalPath);
        return ToDataUri(bytes, extension);
    }

    public static async Task<(string outputHtml, string title)> ConvertHtml(string htmlPath, ILogger logger, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var htmlUri = new Uri(htmlPath);

        (string content, bool isLocal) = await GetContent(htmlUri, httpClient, cancellationToken);

        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(content);

        var title = string.Join('_',
            new[]
            {
                document.QuerySelector("head > title")?.TextContent?.Trim(),
                document.QuerySelector("head > meta[property=\"og:title\"]")?.GetAttribute("content")?.Trim(),
                Path.GetFileNameWithoutExtension(htmlUri.LocalPath).Trim(),
            }
            .FirstOrDefault(x => !string.IsNullOrEmpty(x))
            ?.Split(Path.GetInvalidFileNameChars()) ?? Array.Empty<string>());

        var elements = document.QuerySelectorAll("img")
            .Select(element =>
            {
                var src = element.GetAttribute("src")?.Trim();
                var dataSrc = element.GetAttribute("data-src")?.Trim();
                return (element, src: (!string.IsNullOrWhiteSpace(src) ? src : dataSrc) ?? @"");
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.src) && !x.src.StartsWith("data:"))
            .ToList();
        var srcUrls = elements
            .Select(x => x.src)
            .Distinct()
            .ToList();
        var srcDataUris = (await Task
            .WhenAll(srcUrls
            .Select(async src =>
            {
                var srcUri = new Uri(htmlUri, src);
                byte[] bytes;
                try
                {
                    bytes = await GetBytes(srcUri, httpClient, cancellationToken);
                }
                catch (Exception)
                {
                    return (src, dataUri: src);
                }

                var extension = Path.GetExtension(srcUri.LocalPath);
                var mediaType = GetMediaType(extension, bytes);

                if (mediaType == @"")
                {
                    return (src, dataUri: src);
                }

                var dataUri = ToDataUri(mediaType, bytes);

                return (src, dataUri);
            })))
            .ToDictionary(x => x.src, x => x.dataUri);

        elements.ForEach(x =>
        {
            x.element.SetAttribute("src", srcDataUris[x.src]);
        });

        if (!isLocal)
        {
            (document.DocumentElement.FirstElementChild ?? document.DocumentElement)
                .Insert(AdjacentPosition.AfterBegin, $"<!-- OriginalSrc: {htmlUri} -->");
        }

        return (document.DocumentElement.OuterHtml, title);
    }

    internal static async Task<byte[]> GetBytes(Uri uri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (uri.IsFile)
        {
            return await File.ReadAllBytesAsync(uri.LocalPath, cancellationToken);
        }
        else
        {
            return await httpClient.GetByteArrayAsync(uri, cancellationToken);
        }
    }

    internal static async Task<(string content, bool isLocal)> GetContent(Uri uri, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (uri.IsFile)
        {
            return (await File.ReadAllTextAsync(uri.LocalPath, cancellationToken), true);
        }
        else
        {
            return (await httpClient.GetStringAsync(uri, cancellationToken), false);
        }
    }

    internal static string ToDataUri(byte[] bytes, string extension)
    {
        var mediaType = GetMediaType(extension, bytes);
        return ToDataUri(mediaType, bytes);
    }

    internal static string GetMediaType(string extension, byte[] bytes)
    {
        var mediaType = GetMediaType(extension);
        return mediaType is @"" ? GetMediaType(bytes) : mediaType;
    }

    internal static string GetMediaType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            @".apng" => @"image/apng",

            @".avif" => @"image/avif",

            @".gif" => @"image/gif",

            @".jpeg" or
            @".jpg" or
            @".jfif" or
            @".pjpeg" or
            @".pjp" or
            @".jpe" or
            @".jif" or
            @".jfi" => @"image/jpeg",

            @".png" => @"image/png",

            @".svg" => @"image/svg+xml",

            @".webp" => @"image/webp",

            @".bmp" or
            @".dib" => @"image/bmp",

            @".ico" or
            @".cur" => @"image/x-icon",

            @".tiff" or
            @".tif" => @"image/tiff",

            _ => @"",
        };
    }

    internal static string GetMediaType(byte[] bytes)
    {
        return bytes switch
        {
            //

            // ____ftypavif
            [_, _, _, _, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66, ..] => @"image/avif",

            // GIF87a/GIF89a
            [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, ..] or
            [0x47, 0x49, 0x46, 0x38, 0x37, 0x61, ..] => @"image/gif",

            // ff d8, ff
            [0xFF, 0xD8, 0xFF, ..] => @"image/jpeg",

            // .PNG.... // png, apng
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] =>
                bytes.AsSpan().IndexOf(@"IDAT"u8) is var idat && idat != -1
                && bytes.AsSpan(0, idat).IndexOf(@"acTL"u8) != -1
                ? @"image/apng"
                : @"image/png",

            //

            // RIFF____WEBPVP8
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, ..] => @"image/webp",

            // BM/BA/CI/CP/IC/PT
            [0x42, 0x4D, ..] or
            [0x42, 0x41, ..] or
            [0x43, 0x49, ..] or
            [0x43, 0x50, ..] or
            [0x49, 0x43, ..] or
            [0x50, 0x54, ..] => @"image/bmp",

            // // ico, cur
            [0x00, 0x00, 0x01, 0x00, ..] or
            [0x00, 0x00, 0x02, 0x00, ..] => @"image/x-icon",

            // // tiff
            [0x49, 0x49, 0x2A, 0x00, ..] or
            [0x4D, 0x4D, 0x00, 0x2A, ..] => @"image/tiff",

            // svg
            _ => bytes.AsSpan(0, Math.Min(1000, bytes.Length)).IndexOf(@"<svg"u8) != -1
                || bytes.AsSpan(0, Math.Min(1000, bytes.Length)).IndexOf(@"<SVG"u8) != -1
                ? @"image/svg+xml"
                : @"",
        };
    }

    internal static string ToDataUri(string mediaType, byte[] bytes)
    {
        return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
    }
}
