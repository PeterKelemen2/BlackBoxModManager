<#
.SYNOPSIS
Builds the application on Windows and starts it.

.DESCRIPTION
This script is the Windows counterpart of tools/run-app.sh. The shell script builds for
Windows and starts the application under Wine. This script runs on Windows, so it needs no
Wine and no self-contained publish. It builds the application and starts the executable.

The script does three things:
  1. It checks for the .NET SDK.
  2. It clones the submodules in third_party if they are absent.
  3. It builds the application and starts it.

The window picks the render mode itself. On Windows it picks hardware rendering. Use
-RenderMode to compare hardware and software on one machine. See
src/BlackboxModManager.App/Rendering.cs.

.PARAMETER Configuration
The build configuration. The default is Debug.

.PARAMETER Publish
Publishes a self-contained build into artifacts/app instead of a plain build. The output
directory then holds the .NET runtime, so the build runs on a machine with no SDK.

.PARAMETER NoBuild
Starts the last build. The script skips the build step and the submodule step.

.PARAMETER RenderMode
Sets BLACKBOX_RENDER_MODE for the process. It takes auto, hardware, or software.

.PARAMETER NoWait
Returns after the window opens. The default waits for the window to close and returns the
exit code of the application.

.PARAMETER AppArgs
Arguments for the application. The application reads --themetest, --fonttest, and
--dialogtest. Write them after the parameters of the script. The name -AppArgs is optional.
Never write -- alone as a separator. PowerShell reads that token as an empty parameter name
and stops with an error.

.EXAMPLE
PS> .\tools\run-app.ps1

.EXAMPLE
PS> .\tools\run-app.ps1 -Configuration Release

.EXAMPLE
PS> .\tools\run-app.ps1 --themetest

.NOTES
The execution policy of the machine can block this file. This command runs it one time
without a change to the policy:

  powershell -ExecutionPolicy Bypass -File tools\run-app.ps1
#>

# PositionalBinding stays off on purpose. The application takes arguments that start with a
# dash, and --themetest is one. A positional parameter takes that token and the binder then
# rejects it. With the switch off, every parameter of the script needs its name, and each
# other token goes to AppArgs.
[CmdletBinding(PositionalBinding = $false)]
param(
	[ValidateSet('Debug', 'Release')]
	[string] $Configuration = 'Debug',

	[switch] $Publish,

	[switch] $NoBuild,

	[ValidateSet('auto', 'hardware', 'software')]
	[string] $RenderMode = 'auto',

	[switch] $NoWait,

	[Parameter(ValueFromRemainingArguments = $true)]
	[string[]] $AppArgs
)

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\BlackboxModManager.App\BlackboxModManager.App.csproj'

# One output directory for each mode. A Debug build and a self-contained publish write
# different file sets. A shared directory would keep the files of the other mode.
if ($Publish) {
	$out = Join-Path $root 'artifacts\app'
} else {
	$out = Join-Path $root "artifacts\dev-$Configuration"
}

$exe = Join-Path $out 'BlackboxModManager.exe'

function Stop-WithError {
	param([string] $Message)

	Write-Host "ERROR: $Message" -ForegroundColor Red
	exit 2
}

if (-not $NoBuild) {
	if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
		Stop-WithError 'The dotnet command is not on the PATH. Install the .NET 10 SDK.'
	}

	# The submodules in third_party hold Nikki, Endscript, and CoreExtensions. The build
	# fails without them, and the error names a missing project file.
	#
	# .gitmodules gives the three URLs in SSH form. SSH needs a key and a known_hosts
	# entry, and a fresh Windows machine has neither. The three repositories are public,
	# so this command rewrites the URL to HTTPS for the clone. The rewrite lives in the
	# command. It does not change .gitmodules and it does not change the local
	# configuration.
	$endscript = Join-Path $root 'third_party\Endscript\Endscript\Endscript.csproj'

	if (-not (Test-Path $endscript)) {
		Write-Host 'Clone the submodules in third_party. This takes a minute.'

		& git -C $root -c 'url.https://github.com/.insteadOf=git@github.com:' `
			submodule update --init --recursive

		if ($LASTEXITCODE -ne 0) {
			Stop-WithError 'The clone of the submodules failed.'
		}
	}

	$build = @(
		$project
		'-c', $Configuration
		'-o', $out
		'--nologo'
		'-v', 'quiet'
	)

	if ($Publish) {
		$verb = 'publish'
		$build += @('-r', 'win-x64', '--self-contained', 'true')
	} else {
		$verb = 'build'
	}

	Write-Host "Build the application in $Configuration."

	# The three third-party projects emit 15 warnings. Hold the output and show it only
	# when the build fails.
	$log = & dotnet $verb @build 2>&1 | Out-String

	if ($LASTEXITCODE -ne 0) {
		Write-Host $log
		Stop-WithError "The $verb failed."
	}
}

if (-not (Test-Path $exe)) {
	Stop-WithError "The file $exe does not exist. Run the script again without -NoBuild."
}

# Rendering.Apply reads this variable. An empty value reads as auto.
if ($RenderMode -eq 'auto') {
	$env:BLACKBOX_RENDER_MODE = $null
} else {
	$env:BLACKBOX_RENDER_MODE = $RenderMode
}

$start = @{
	FilePath    = $exe
	PassThru    = $true
	WorkingDirectory = $out
}

# Start-Process rejects an empty argument list.
if ($AppArgs) {
	$start.ArgumentList = $AppArgs
}

Write-Host "Start $exe."

$process = Start-Process @start

if ($NoWait) {
	Write-Host "The application runs as process $($process.Id)."
	exit 0
}

# PowerShell does not wait for a window application. Wait for the handle, so that the exit
# code of the application becomes the exit code of the script.
$process.WaitForExit()

exit $process.ExitCode
