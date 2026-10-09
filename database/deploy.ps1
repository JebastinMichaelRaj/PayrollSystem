param(
    [string]$Server = "localhost",
    [string]$User,
    [string]$Password
)

$ErrorActionPreference = "Stop"

$auth = @()

if ($User) {
    $auth += @("-U", $User, "-P", $Password)
}
else {
    $auth += "-E"
}

$sqlFiles = Get-ChildItem -Path $PSScriptRoot -Filter "0*.sql" |
    Sort-Object Name

foreach ($file in $sqlFiles) {
    Write-Host "Running $($file.Name)..."

    $sqlArgs = @("-S", $Server) + $auth + @(
        "-b",
        "-i", $file.FullName
    )

    & sqlcmd @sqlArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Failed: $($file.Name)"
    }
}

Write-Host "Database deployed."