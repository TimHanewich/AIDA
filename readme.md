# AIDA: AI Desktop Assistant
**AIDA** is a .NET console application that uses the [TimHanewich.AgentFramework](https://github.com/TimHanewich/TimHanewich.AgentFramework) library. It has several capabilities.

To run AIDA, you must first:
1. Install the .NET 9.0 SDK.
2. Download AIDA.exe [here](https://github.com/TimHanewich/TimHanewich.AgentFramework/releases/download/1/AIDA.exe). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

## Download AIDA
You can download AIDA below. Check out [the changelog](./changelog.md) to learn more about each version.

|Version|Windows|Linux|
|-|-|-|
|0.5.0|[download](https://github.com/TimHanewich/AIDA/releases/download/1/AIDA_0.5.0_windows.zip)|[download](https://github.com/TimHanewich/AIDA/releases/download/1/AIDA_0.5.0_linux.zip)|

## Building the Project
For windows:
```
dotnet publish AIDA.csproj -c Release --self-contained false
```

For linux:
```
dotnet publish AIDA.csproj -c Release -r linux-x64 --self-contained false
```

On April 23, 2025, I ran into an issue with .NET 9.0 in building (and `dotnet restore`) with self-contained being True. Changing to False fixed the issue.