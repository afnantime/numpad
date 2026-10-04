$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Missing .NET Framework C# compiler.' }
$expected = 'CF929977462E4C53C3F211E743F548BDB41AE20E80166E314EB8A7A68BCD05A7'
if ((Get-FileHash -LiteralPath 'vendor\VirtualDesktop.cs' -Algorithm SHA256).Hash -ne $expected) { throw 'Pinned upstream source checksum mismatch.' }
New-Item -ItemType Directory -Force dist | Out-Null
& $compiler /nologo /target:library /optimize+ /out:dist\VirtualDesktop.dll vendor\VirtualDesktop.cs
if ($LASTEXITCODE -ne 0) { throw 'VirtualDesktop compilation failed.' }
$references = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Runtime.Serialization.dll', '/r:dist\VirtualDesktop.dll')
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:app.manifest /out:dist\NumpadDesktop.exe $references Core.cs KeyboardHook.cs App.cs
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& $compiler /nologo /target:exe /platform:anycpu /win32manifest:app.manifest /out:dist\NumpadDesktop.Tests.exe $references Core.cs KeyboardHook.cs Tests.cs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath README.md -Destination dist\README.md
Copy-Item -LiteralPath vendor\LICENSE -Destination dist\VirtualDesktop.LICENSE.txt
Compress-Archive -LiteralPath dist\NumpadDesktop.exe, dist\VirtualDesktop.dll, dist\README.md, dist\VirtualDesktop.LICENSE.txt -DestinationPath dist\NumpadDesktop-Windows10.zip -Force
Write-Output 'Built dist\NumpadDesktop.exe'
