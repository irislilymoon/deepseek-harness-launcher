$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& $csc /nologo /target:winexe /platform:anycpu `
  /out:"$root\DeepSeek Harness.exe" `
  /win32icon:"$root\assets\deepseek.ico" `
  /win32manifest:"$root\app.manifest" `
  /resource:"$root\assets\deepseek-color.png",DeepSeekRunner.deepseek-color.png `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  "$root\Program.cs"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "OK -> $root\DeepSeek Harness.exe"
