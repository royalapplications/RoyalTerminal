[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$Thumbprint,[uri]$TimestampServer,[switch]$TestIdentity)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($env:OS -ne 'Windows_NT') {throw 'Run this packager on Windows.'}
if (!$TestIdentity -and (!$TimestampServer -or $TimestampServer.Scheme -ne 'https')) {throw 'Release signing requires an explicit HTTPS RFC3161 timestamp service.'}
$src=(Resolve-Path -LiteralPath $PublishDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $src 'RoyalTerminal.Multiplexing.Desktop.exe') -PathType Leaf)) {throw 'Missing native workspace launcher.'}
if (Test-Path -LiteralPath $OutputDirectory) {throw 'Use a new output directory.'}
$cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$Thumbprint"
$eku=@($cert.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.37'} | ForEach-Object {$_.EnhancedKeyUsages | ForEach-Object {$_.Value}})
if (!$cert.HasPrivateKey -or $cert.NotBefore -gt (Get-Date) -or $cert.NotAfter -lt (Get-Date) -or $eku -notcontains '1.3.6.1.5.5.7.3.3') {throw 'A valid code-signing certificate with private key is required.'}
$tool=(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object {[version]$_.Directory.Parent.Name} -Descending | Select-Object -First 1).FullName
function SignChecked([string]$file) {
  $stamps=@();if($TimestampServer){$stamps=@('/tr',$TimestampServer.AbsoluteUri,'/td','SHA256')}
  & $tool sign /fd SHA256 /sha1 $Thumbprint @stamps $file;if($LASTEXITCODE){throw "Signing failed: $file"}
  & $tool verify /pa $file;if($LASTEXITCODE){throw "Invalid signature: $file"}
  if((Get-AuthenticodeSignature -LiteralPath $file).SignerCertificate.Thumbprint -ne $Thumbprint){throw 'Wrong signing identity.'}
}
$dest=(New-Item -ItemType Directory -Path $OutputDirectory).FullName;$payload=(New-Item -ItemType Directory -Path (Join-Path $dest 'payload')).FullName
if(Get-ChildItem $src -Recurse -Force -Attributes ReparsePoint){throw 'Linked publish items are not supported.'}
Get-ChildItem -LiteralPath $src -Force | Copy-Item -Destination $payload -Recurse
SignChecked (Join-Path $payload 'RoyalTerminal.Multiplexing.Desktop.exe')
$catalog=Join-Path $dest 'workspace.cat';New-FileCatalog -Path $payload -CatalogFilePath $catalog -CatalogVersion 2.0 | Out-Null
SignChecked $catalog
if((Test-FileCatalog -Path $payload -CatalogFilePath $catalog) -ne 'Valid'){throw 'The payload differs from its signed catalog.'}
Compress-Archive -Path $payload,$catalog -DestinationPath (Join-Path $dest 'workspace-win-x64.zip')
