using System.Diagnostics;
using System.IO;
using System.Text;

namespace EndfieldChargePlus.Assistant;

/// <summary>Local input transport; policy validation and DPAPI storage remain in the shared Hub.</summary>
public sealed class GeminiCredentialClient
{
    private readonly string _root;
    public GeminiCredentialClient(string? root = null) => _root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChatGPT", "GeminiHub");

    public async Task ConfigureAsync(string key, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160) throw new InvalidOperationException("請輸入 Gemini API Key。");
        await RunAsync("Import-GeminiKeyFromStdin.ps1", key.Trim(), cancellation);
        try { await RunAsync("Restart-GeminiHub.ps1", null, cancellation); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("金鑰已加密儲存，但 AI 服務未能重新啟動。請稍後重新套用。"); }
    }

    public async Task ConfigureFreeAsync(string key, bool freeConfirmed, CancellationToken cancellation)
    {
        if (!freeConfirmed) throw new InvalidOperationException(LocalizationManager.Text("請先確認此金鑰的專案使用免費方案，且未啟用付費。", "Confirm that this key's project is on the free tier with billing disabled."));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160) throw new InvalidOperationException("請輸入 Gemini API Key。");
        await RunAsync("Configure-GeminiHubFromStdin.ps1", key.Trim(), cancellation, "-FreeConfirmed");
        try { await RunAsync("Restart-GeminiHub.ps1", null, cancellation); }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException("金鑰已加密儲存，但 AI 服務未能重新啟動。請稍後重新套用。"); }
    }

    public Task EnsureStartedAsync(CancellationToken cancellation) => RunAsync("Ensure-GeminiHub.ps1", null, cancellation);

    private async Task RunAsync(string script, string? input, CancellationToken cancellation, string? inputFlag = "-ReplaceExisting")
    {
        var file = Path.Combine(_root, script);
        if (!File.Exists(file)) throw new InvalidOperationException("找不到共用 Gemini 服務的設定工具。");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _root, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false) };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File"); start.ArgumentList.Add(file);
        if (input is not null && inputFlag is not null) start.ArgumentList.Add(inputFlag);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("無法開啟本機金鑰設定工具。");
        // Drain output without putting credential-helper errors or secrets into app logs.
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), cancellation);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation);
            await output; await errors;
            if (process.ExitCode == 2) throw new InvalidOperationException("金鑰格式不正確，或不屬於目前已核對的 Gemini 專案。原金鑰保留。");
            if (process.ExitCode != 0) throw new InvalidOperationException("無法儲存金鑰；請確認共用 AI 服務的檔案可寫入。");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
        finally { input = null; }
    }
}
