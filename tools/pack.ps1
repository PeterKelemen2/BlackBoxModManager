<#
.SYNOPSIS
Builds the release packages of this application.

.DESCRIPTION
This script runs the three stages that turn a source tree into a release.

  1. It publishes the application, framework-dependent, into a staging directory.
  2. It checks that every file which a license obliges us to ship is in that directory.
  3. It runs vpk pack, which writes Setup.exe, a portable zip, and a feed package.

The release workflow calls this script. A developer can call it too, to read what a release
holds before a tag goes out.

**This script needs the Velopack CLI, and it does not install it.** Run this one time:

  dotnet tool install -g vpk

The workflow installs it in a step of its own. One place installs the tool, so a developer and
the runner both see the same failure when it is absent.

**The release is framework-dependent.** Setup.exe asks Microsoft for the .NET 10 Desktop
Runtime and installs it. A Wine user gets no such install and must put that runtime into the
prefix. See docs/roadmap/03-wine-verification.md.

.PARAMETER Version
The version of this release, as SemVer 2, with no leading v. Examples: 0.1.0 and
0.1.0-alpha.1.

Velopack refuses a version that is not SemVer 2. A four-part number such as 0.1.0.0 is not
SemVer 2.

.PARAMETER PackDir
The staging directory that stage 1 writes and stage 3 reads.

.PARAMETER OutputDir
The directory that receives Setup.exe, the portable zip, and the feed package.

.PARAMETER SkipPublish
Skips stage 1 and packs the staging directory that is already there.

.EXAMPLE
PS> .\tools\pack.ps1 -Version 0.1.0-alpha.1

.NOTES
The execution policy of the machine can block this file. This command runs it one time
without a change to the policy:

  powershell -ExecutionPolicy Bypass -File tools\pack.ps1 -Version 0.1.0-alpha.1
#>

[CmdletBinding()]
param(
	[Parameter(Mandatory = $true)]
	[string] $Version,

	[string] $PackDir,

	[string] $OutputDir,

	[switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\BlackboxModManager.App\BlackboxModManager.App.csproj'
$icon = Join-Path $root 'src\BlackboxModManager.App\Assets\icon.ico'

if (-not $PackDir) { $PackDir = Join-Path $root 'artifacts\pack' }
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts\releases' }

function Stop-WithError {
	param([string] $Message)

	Write-Host "ERROR: $Message" -ForegroundColor Red
	exit 2
}

# ---------------------------------------------------------------- the version

# Velopack reads SemVer 2. Reject anything else here, and not after a five-minute build.
#
# This tests with -match and never with -notmatch. Only -match fills $Matches in a way that
# reads clearly, and the two capture groups below come out of it.
if ($Version -match '^(\d+\.\d+\.\d+)(?:-([0-9A-Za-z.-]+))?$') {
	$prefix = $Matches[1]
	$suffix = if ($Matches[2]) { $Matches[2] } else { '' }
} else {
	Stop-WithError "The version $Version is not SemVer 2. Write 1.2.3 or 1.2.3-alpha.1."
}

# The prefix and the suffix go in as two properties, and never as one Version.
# AssemblyVersion holds four numbers and it cannot hold a prerelease label. See
# src/Directory.Build.props.
Write-Host "Pack version $Version (prefix $prefix, suffix '$suffix')."

# ---------------------------------------------------------------- stage 1, publish

if (-not $SkipPublish) {
	if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
		Stop-WithError 'The dotnet command is not on the PATH. Install the .NET 10 SDK.'
	}

	# The submodules in third_party hold Nikki, Endscript, and CoreExtensions. The build fails
	# without them, and the error names a missing project file. This matches tools/run-app.ps1.
	$endscript = Join-Path $root 'third_party\Endscript\Endscript\Endscript.csproj'

	if (-not (Test-Path $endscript)) {
		Write-Host 'Clone the submodules in third_party. This takes a minute.'

		& git -C $root submodule update --init --recursive

		if ($LASTEXITCODE -ne 0) { Stop-WithError 'The clone of the submodules failed.' }
	}

	# A stale file of an earlier publish must never reach a release.
	if (Test-Path $PackDir) { Remove-Item $PackDir -Recurse -Force }

	# Never add PublishSingleFile here.
	#
	# Velopack wants a directory of loose files, and it builds the single distributable itself.
	# That also removes the problem of step 3, fact 4: LZCompressLib.dll lands beside the host
	# as a normal file, which is what the P/Invoke by name needs.
	$publish = @(
		$project
		'-c', 'Release'
		'-r', 'win-x64'
		'--self-contained', 'false'
		'-o', $PackDir
		"-p:VersionPrefix=$prefix"
		"-p:VersionSuffix=$suffix"
		'--nologo'
		'-v', 'quiet'
	)

	Write-Host 'Publish the application.'

	# The three third-party projects emit about 21 warnings. Hold the output and show it only
	# when the publish fails.
	$log = & dotnet publish @publish 2>&1 | Out-String

	if ($LASTEXITCODE -ne 0) {
		Write-Host $log
		Stop-WithError 'The publish failed.'
	}
}

if (-not (Test-Path $PackDir)) {
	Stop-WithError "The directory $PackDir does not exist. Run this script without -SkipPublish."
}

# ---------------------------------------------------------------- stage 2, the content gate

# Every file that a license obliges us to ship, and the two native files that the application
# cannot run without.
#
# **This gate fails the release.** A missing license text is a license breach, and a missing
# LZCompressLib.dll is an application that throws on the first container save. Neither one may
# reach a user, and neither one is visible in a directory listing that nobody reads.
#
# The four .ttf files are a Resource and not Content. They live inside BlackboxModManager.dll,
# so they never appear here. Do not add them.
$required = @(
	'BlackboxModManager.exe'
	'BlackboxModManager.dll'
	'BlackboxModManager.Core.dll'
	'BlackboxModManager.runtimeconfig.json'
	# Native, x64, and loaded by name. See BlackboxModManager.App.csproj.
	'LZCompressLib.dll'
	# 7-Zip, under the GNU LGPL. The release asks a redistribution in binary form to
	# reproduce its license information, so all four files travel together.
	'7-Zip\7z.exe'
	'7-Zip\7z.dll'
	'7-Zip\7-Zip-License.txt'
	'7-Zip\7-Zip-readme.txt'
	# The license of this application, and the notices of everything that it ships.
	'LICENSE'
	'THIRD-PARTY-NOTICES.md'
	# The SIL Open Font License asks a copy that bundles a font to carry the license.
	'Fonts\Inter-OFL.txt'
	'Fonts\JetBrainsMono-OFL.txt'
	'Fonts\IBMPlexSans-OFL.txt'
)

$missing = @()

foreach ($name in $required) {
	if (-not (Test-Path (Join-Path $PackDir $name) -PathType Leaf)) { $missing += $name }
}

if ($missing.Count -gt 0) {
	Write-Host "ERROR: $($missing.Count) required files are absent from $PackDir." -ForegroundColor Red

	foreach ($name in $missing) { Write-Host "  $name" -ForegroundColor Red }

	Stop-WithError 'The release must not go out without these files.'
}

Write-Host "The content gate passed. $($required.Count) required files are present."

# The GC mode travels in runtimeconfig.json. Step 14 tuned the container save around server
# collection, so a release that lost the setting would be slower for a reason nobody sees.
$config = Get-Content (Join-Path $PackDir 'BlackboxModManager.runtimeconfig.json') -Raw |
	ConvertFrom-Json

if ($config.runtimeOptions.configProperties.'System.GC.Server' -ne $true) {
	Stop-WithError 'System.GC.Server is not set in runtimeconfig.json. Step 14 needs it.'
}

# ---------------------------------------------------------------- stage 3, vpk pack

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
	Stop-WithError 'The vpk command is not on the PATH. Run: dotnet tool install -g vpk'
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# --packId is the identity of the install, and it names the directory under %LOCALAPPDATA%.
#
# **Never change it.** An installed copy looks for its own id in the feed. A new id makes every
# existing install stop seeing updates, and neither side reports an error. The casing matches
# AppPaths.FolderName, so one name means one thing.
#
# --mainExe takes a file name and not a path. AssemblyName is BlackboxModManager, with a lower
# case b in box, and the pack id has a capital B. That difference is old. Do not "fix"
# AssemblyName, because that renames the executable that every script and document names.
#
# --channel stays out. The default for a Windows build is win, and UpdateService reads that
# same default. Naming it in one place and not the other is how a feed goes silently empty.
#
# --icon points at the application's own icon. Setup.exe, the shortcuts, and the
# Add/Remove Programs entry then carry it, instead of the Velopack default. The file is
# original artwork of this project, so it raises no THIRD-PARTY-NOTICES.md question.
#
# --framework makes Setup.exe install the runtime, because this build is framework-dependent.
# **The value net10.0-x64-desktop is not in the Velopack documentation, which lists 5.0 to
# 9.0.** The documentation says that every version from 5.0 up works. Read the output of this
# command, and test the installer on a machine with no .NET 10.
$pack = @(
	'pack'
	'--packId', 'BlackBoxModManager'
	'--packVersion', $Version
	'--packDir', $PackDir
	'--mainExe', 'BlackboxModManager.exe'
	'--packTitle', 'BlackBox Mod Manager'
	'--packAuthors', 'Peter Kelemen'
	'--outputDir', $OutputDir
	'--runtime', 'win-x64'
	'--framework', 'net10.0-x64-desktop'
	'--icon', $icon
)

Write-Host 'Run vpk pack.'

& vpk @pack

if ($LASTEXITCODE -ne 0) { Stop-WithError 'vpk pack failed.' }

Write-Host ''
Write-Host "The release of version $Version is in $OutputDir." -ForegroundColor Green

Get-ChildItem $OutputDir | Select-Object Name, Length | Format-Table -AutoSize
