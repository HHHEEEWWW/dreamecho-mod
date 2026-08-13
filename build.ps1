# 构建并部署 DreamEchoMod 插件（一条命令）
$GameDir = 'E:\steam\steamapps\common\DreamEcho'
$Proj = 'E:\AI work\item-box\code\dreamecho-mod'
$Runtime = 'net6.0'
dotnet build "$Proj\src\DreamEchoMod\DreamEchoMod.csproj" -c Release
if ($LASTEXITCODE -ne 0) { Write-Host '构建失败'; exit 1 }
Copy-Item "$Proj\src\DreamEchoMod\bin\Release\$Runtime\DreamEchoMod.dll" "$GameDir\BepInEx\plugins\" -Force
Write-Host '部署完成: DreamEchoMod.dll -> BepInEx\plugins\'
