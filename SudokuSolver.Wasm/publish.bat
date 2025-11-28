@echo off
if exist ".\publish" (
    echo Deleting publish folder...
    rmdir /s /q ".\publish"
)
dotnet publish -c Release -o ./publish -p:RunAOTCompilation=true -p:PublishTrimmed=true -p:InvariantGlobalization=true
