@echo off
rem Secure File Explorer 起動（共有の最新版を取得して実行）
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Launch.ps1"
