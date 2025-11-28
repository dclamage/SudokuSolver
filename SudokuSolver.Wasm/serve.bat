@echo off
dotnet serve -d .\publish\wwwroot -p 8000 -h "Cross-Origin-Opener-Policy:same-origin" -h "Cross-Origin-Embedder-Policy:require-corp"
