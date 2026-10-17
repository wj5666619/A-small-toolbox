@echo off
chcp 65001 >nul
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (echo [错误] 未找到 .NET Framework 4.8 编译器 csc.exe & pause & exit /b 1)

echo [1/2] 编译主程序 小工具箱.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /win32icon:定时关机.ico /resource:gcm_tool.zip,GcmZip /resource:sam_tool.zip,SamZip /resource:clean_tool.zip,CleanZip /resource:video_tool.zip,VideoZip /resource:dnkh_tool.zip,DnkhZip /out:小工具箱.exe 定时关机.cs
if errorlevel 1 (echo [错误] 主程序编译失败 & pause & exit /b 1)

echo [2/2] 编译安装器 小工具箱_安装程序.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /win32icon:定时关机.ico /resource:小工具箱.exe,MainExe /resource:sam_tool.zip,SamZip /resource:clean_tool.zip,CleanZip /resource:ffmpeg_tool.zip,FfmpegZip /out:小工具箱_安装程序.exe 安装程序.cs
if errorlevel 1 (echo [错误] 安装器编译失败 & pause & exit /b 1)

echo 构建完成！
pause
