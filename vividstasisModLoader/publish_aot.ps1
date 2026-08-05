$env:PATH="F:\env\msvc\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64;F:\env\msvc\Windows Kits\10\bin\10.0.28000.0\x64;" + $env:PATH
$env:INCLUDE="F:\env\msvc\VC\Tools\MSVC\14.51.36231\include;F:\env\msvc\Windows Kits\10\Include\10.0.28000.0\ucrt;F:\env\msvc\Windows Kits\10\Include\10.0.28000.0\shared;F:\env\msvc\Windows Kits\10\Include\10.0.28000.0\um;F:\env\msvc\Windows Kits\10\Include\10.0.28000.0\winrt;F:\env\msvc\Windows Kits\10\Include\10.0.28000.0\cppwinrt"
$env:LIB="F:\env\msvc\VC\Tools\MSVC\14.51.36231\lib\x64;F:\env\msvc\Windows Kits\10\Lib\10.0.28000.0\ucrt\x64;F:\env\msvc\Windows Kits\10\Lib\10.0.28000.0\um\x64"

dotnet publish -c Release /p:IlcUseEnvironmentalTools=true
