@echo off
chcp 65001 > nul
rem Translator relay: double-click before playing VRChat and keep this window open.
cd /d "%~dp0"
python pc_osc_relay.py
pause
