@echo off
if "%~1"=="" (
    echo Drag one or more video files onto this script to convert them.
    pause
    exit /b
)

for %%F in (%*) do (
    echo Converting: %%~fF
    echo Output:     %%~dpF%%~nF_switch.mp4
    echo.

    ffmpeg -i "%%~fF" -vf "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,setsar=1" -r 30 -c:v libx264 -profile:v baseline -level:v 4.1 -crf 23 -preset fast -g 30 -bf 0 -c:a aac -b:a 192k -movflags +faststart "%%~dpF%%~nF_switch.mp4"

    echo.
    echo Done: %%~nF_switch.mp4
    echo ----------------------------------------
)

echo.
echo All files converted!
pause