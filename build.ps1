$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:TokenTracker.exe /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll TokenTracker.cs
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
