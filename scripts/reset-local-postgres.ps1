$ErrorActionPreference = 'Stop'
$taskBin = 'C:/Program Files/PostgreSQL/18/bin'
$taskData = 'C:/Program Files/PostgreSQL/18/data'
$taskHba = Join-Path $taskData 'pg_hba.conf'
$taskOriginal = [System.IO.File]::ReadAllBytes($taskHba)
$taskConfig = Get-Content (Join-Path $PSScriptRoot '../src/Aynera.Api/appsettings.json') -Raw | ConvertFrom-Json
$taskParts = @{}
foreach ($taskPart in $taskConfig.ConnectionStrings.Aynera.Split(';')) {
    $taskPair = $taskPart.Split('=', 2)
    if ($taskPair.Length -eq 2) { $taskParts[$taskPair[0].Trim()] = $taskPair[1] }
}
$taskPassword = $taskParts['Password']
if ([string]::IsNullOrEmpty($taskPassword)) { throw 'Configured password is missing.' }
try {
    $taskRule = [System.Text.Encoding]::UTF8.GetBytes("host postgres postgres 127.0.0.1/32 trust`r`n")
    [System.IO.File]::WriteAllBytes($taskHba, [byte[]]($taskRule + $taskOriginal))
    $taskEscaped = $taskPassword.Replace("'", "''")
    "SET standard_conforming_strings = on; ALTER ROLE postgres PASSWORD '$taskEscaped';" |
        & "$taskBin/psql.exe" -X -w -h 127.0.0.1 -U postgres -d postgres -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'Password reset failed.' }
} finally {
    [System.IO.File]::WriteAllBytes($taskHba, $taskOriginal)
}
$env:PGPASSWORD = $taskPassword
try {
    & "$taskBin/psql.exe" -X -w -h 127.0.0.1 -U postgres -d postgres -f (Join-Path $PSScriptRoot 'create-test-db.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Test database setup failed.' }
} finally { Remove-Item Env:PGPASSWORD }

