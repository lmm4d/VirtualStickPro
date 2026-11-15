@echo off
chcp 65001 >nul
title TouchKeys 启动器
cd /d "%~dp0"

if not exist "TouchKeys.exe" (
    echo 找不到 TouchKeys.exe
    pause
    exit /b 1
)

echo ============================================
echo   TouchKeys 触屏虚拟摇杆 + 自定义按键
echo ============================================
echo.
echo   Ctrl+Alt+F2  切换 编辑 / 游玩 模式
echo   编辑模式里：拖动移动、滚轮缩放、右键菜单
echo   游玩模式里：空白区域完全穿透，不挡触摸
echo.
echo  正在启动...
echo.

start "" "TouchKeys.exe"
timeout /t 2 >nul
exit /b 0
