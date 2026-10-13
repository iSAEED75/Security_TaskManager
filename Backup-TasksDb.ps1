# این اسکریپت را پیش از اولین اجرای نسخه جدید، روی همان رایانه‌ای که SQL Server Express نصب است اجرا کنید.
# پیش‌نیاز: ابزار sqlcmd در PATH ویندوز باشد و کاربر اجازه BACKUP DATABASE داشته باشد.
$ErrorActionPreference = 'Stop'
$server = '.\SQLEXPRESS'
$database = 'TasksDb'
$localFolder = Join-Path $PSScriptRoot 'DatabaseBackups'
New-Item -ItemType Directory -Force -Path $localFolder | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'

# مسیر پشتیبان پیش‌فرض SQL Server را می‌گیریم تا سرویس SQL به پوشه مقصد دسترسی داشته باشد.
$serverFolder = (sqlcmd -S $server -E -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));" | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($serverFolder)) {
    throw 'مسیر پیش‌فرض پشتیبان SQL Server خوانده نشد. نسخه جدید را تا تهیه پشتیبان اجرا نکنید.'
}
$serverBackupFile = Join-Path $serverFolder "${database}_${stamp}.bak"
$sqlPath = $serverBackupFile.Replace("'", "''")
Write-Host "در حال تهیه نسخه پشتیبان از پایگاه داده $database ..."
sqlcmd -S $server -E -b -Q "BACKUP DATABASE [$database] TO DISK = N'$sqlPath' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;"
if ($LASTEXITCODE -ne 0) { throw 'تهیه نسخه پشتیبان پایگاه داده ناموفق بود. تغییرات برنامه را اجرا نکنید.' }

# یک کپی محلی کنار پروژه نیز نگه می‌داریم.
Copy-Item -LiteralPath $serverBackupFile -Destination (Join-Path $localFolder (Split-Path $serverBackupFile -Leaf)) -Force
Write-Host "نسخه پشتیبان تهیه شد: $serverBackupFile" -ForegroundColor Green
Write-Host "کپی کنار پروژه: $localFolder" -ForegroundColor Green
