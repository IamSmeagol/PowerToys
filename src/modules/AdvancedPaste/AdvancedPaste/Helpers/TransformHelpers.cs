﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

using AdvancedPaste.Models;
using ManagedCommon;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AdvancedPaste.Helpers;

public static class TransformHelpers
{
    public static async Task<DataPackage> TransformAsync(PasteFormats format, DataPackageView clipboardData)
    {
        return format switch
        {
            PasteFormats.PlainText => ToPlainText(clipboardData),
            PasteFormats.Markdown => ToMarkdown(clipboardData),
            PasteFormats.Json => ToJson(clipboardData),
            PasteFormats.ImageToText => await ImageToTextAsync(clipboardData),
            PasteFormats.PasteAsTxtFile => await ToTxtFileAsync(clipboardData),
            PasteFormats.PasteAsPngFile => await ToPngFileAsync(clipboardData),
            PasteFormats.PasteAsHtmlFile => await ToHtmlFileAsync(clipboardData),
            PasteFormats.KernelQuery => throw new ArgumentException($"Unsupported format {format}", nameof(format)),
            _ => throw new ArgumentException($"Unknown value {format}", nameof(format)),
        };
    }

    private static DataPackage ToPlainText(DataPackageView clipboardData)
    {
        Logger.LogTrace();
        return CreateDataPackageFromText(MarkdownHelper.PasteAsPlainTextFromClipboard(clipboardData));
    }

    private static DataPackage ToMarkdown(DataPackageView clipboardData)
    {
        Logger.LogTrace();
        return CreateDataPackageFromText(MarkdownHelper.ToMarkdown(clipboardData));
    }

    private static DataPackage ToJson(DataPackageView clipboardData)
    {
        Logger.LogTrace();
        return CreateDataPackageFromText(JsonHelper.ToJsonFromXmlOrCsv(clipboardData));
    }

    private static async Task<DataPackage> ImageToTextAsync(DataPackageView clipboardData)
    {
        Logger.LogTrace();

        var bitmap = await ClipboardHelper.GetClipboardImageContentAsync(clipboardData) ?? throw new ArgumentException("No image content found in clipboard", nameof(clipboardData));
        var text = await OcrHelpers.ExtractTextAsync(bitmap);
        return CreateDataPackageFromText(text);
    }

    private static async Task<DataPackage> ToPngFileAsync(DataPackageView clipboardData)
    {
        Logger.LogTrace();

        var clipboardBitmap = await ClipboardHelper.GetClipboardImageContentAsync(clipboardData);

        using var pngStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
        encoder.SetSoftwareBitmap(clipboardBitmap);
        await encoder.FlushAsync();

        return await CreateDataPackageFromFileContentAsync(pngStream.AsStreamForRead(), "png");
    }

    private static async Task<DataPackage> ToTxtFileAsync(DataPackageView clipboardData)
    {
        Logger.LogTrace();

        var text = await ClipboardHelper.GetClipboardTextOrHtmlTextAsync(clipboardData);
        return await CreateDataPackageFromFileContentAsync(text, "txt");
    }

    private static async Task<DataPackage> ToHtmlFileAsync(DataPackageView clipboardData)
    {
        Logger.LogTrace();

        var cfHtml = await ClipboardHelper.GetClipboardHtmlContentAsync(clipboardData);
        var html = RemoveHtmlMetadata(cfHtml);

        return await CreateDataPackageFromFileContentAsync(html, "html");
    }

    /// <summary>
    /// Removes leading CF_HTML metadata from HTML clipboard data.
    /// See: https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format
    /// </summary>
    private static string RemoveHtmlMetadata(string cfHtml)
    {
        int? GetIntTagValue(string tagName)
        {
            var tagNameWithColon = tagName + ":";
            int tagStartPos = cfHtml.IndexOf(tagNameWithColon, StringComparison.InvariantCulture);

            const int tagValueLength = 10;
            return tagStartPos != -1 && int.TryParse(cfHtml.AsSpan(tagStartPos + tagNameWithColon.Length, tagValueLength), CultureInfo.InvariantCulture, out int result) ? result : null;
        }

        var startFragmentIndex = GetIntTagValue("StartFragment");
        var endFragmentIndex = GetIntTagValue("EndFragment");

        return (startFragmentIndex == null || endFragmentIndex == null) ? cfHtml : cfHtml[startFragmentIndex.Value..endFragmentIndex.Value];
    }

    private static async Task<DataPackage> CreateDataPackageFromFileContentAsync(string data, string fileExtension)
    {
        if (string.IsNullOrEmpty(data))
        {
            throw new ArgumentException($"Empty value in {nameof(CreateDataPackageFromFileContentAsync)}", nameof(data));
        }

        var path = GetPasteAsFileTempFilePath(fileExtension);

        await File.WriteAllTextAsync(path, data);
        return await ClipboardHelper.CreateDataPackageFromFileContentAsync(path);
    }

    private static async Task<DataPackage> CreateDataPackageFromFileContentAsync(Stream stream, string fileExtension)
    {
        var path = GetPasteAsFileTempFilePath(fileExtension);

        using var fileStream = File.Create(path);
        await stream.CopyToAsync(fileStream);

        return await ClipboardHelper.CreateDataPackageFromFileContentAsync(path);
    }

    private static string GetPasteAsFileTempFilePath(string fileExtension)
    {
        var prefix = ResourceLoaderInstance.ResourceLoader.GetString("PasteAsFile_FilePrefix");
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        return Path.Combine(Path.GetTempPath(), $"{prefix}{timestamp}.{fileExtension}");
    }

    private static DataPackage CreateDataPackageFromText(string content) => ClipboardHelper.CreateDataPackageFromText(content);
}