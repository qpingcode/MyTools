# 如果当前Mytools已经启动，则杀掉进程
$processName = "MyTools.Desktop"
$process = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "正在关闭进程 $processName..."
    Stop-Process -Name $processName -Force
    Start-Sleep -Seconds 2
} else {
    Write-Host "$processName 进程未运行"
}