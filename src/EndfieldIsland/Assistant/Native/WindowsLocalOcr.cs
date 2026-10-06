using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace EndfieldChargePlus.Assistant.Native;

/// <summary>Reads only the explicitly attached PNG, using Windows' installed OCR languages.</summary>
internal static class WindowsLocalOcr
{
    internal static string[] Languages()=>OcrEngine.AvailableRecognizerLanguages.Select(l=>l.LanguageTag).ToArray();
    internal static async Task<string> ReadAsync(ImageInput image,CancellationToken token)
    {
        NativeBrain.ValidateImage(image);token.ThrowIfCancellationRequested();
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try{
            var engine=OcrEngine.TryCreateFromLanguage(new Language("zh-Hant"))??OcrEngine.TryCreateFromUserProfileLanguages();
            if(engine is null)throw new AssistantFailure("image_ocr_language_missing");
            using var stream=new InMemoryRandomAccessStream();
            using(var writer=new DataWriter(stream)){
                writer.WriteBytes(Convert.FromBase64String(image.data));
                await writer.StoreAsync().AsTask(deadline.Token);writer.DetachStream();
            }
            stream.Seek(0);
            var decoder=await BitmapDecoder.CreateAsync(stream).AsTask(deadline.Token);
            if(decoder.PixelWidth>OcrEngine.MaxImageDimension||decoder.PixelHeight>OcrEngine.MaxImageDimension)throw new AssistantFailure("image_dimensions_limit");
            using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore).AsTask(deadline.Token);
            var result=await engine.RecognizeAsync(bitmap).AsTask(deadline.Token);
            // Windows inserts spaces between some Chinese words; keep Latin word spacing and line breaks.
            var text=Regex.Replace(string.Join("\n",result.Lines.Select(l=>l.Text.Trim())),@"(?<=[\u3400-\u9fff])[ \t]+(?=[\u3400-\u9fff])","").Trim();
            if(text.Length==0)throw new AssistantFailure("image_ocr_no_text");
            if(text.Length>16000)throw new AssistantFailure("image_ocr_too_much_text");
            return text;
        }catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(OperationCanceledException){throw new AssistantFailure("image_ocr_timeout");}
        catch(AssistantFailure){throw;}
        catch{throw new AssistantFailure("image_ocr_unavailable");}
    }
}
