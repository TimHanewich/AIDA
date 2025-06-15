# AIDA: AI Desktop Assistant
**AIDA** is a .NET console application that uses the [TimHanewich.AgentFramework](https://github.com/TimHanewich/TimHanewich.AgentFramework) library. It has several capabilities.

To run AIDA, you must first:
1. Install the [.NET 9.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
2. Download AIDA (see below). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

## Download AIDA
You can download and use AIDA yourself, for free! Visit [the changelog page](./changelog.md) for download links!

## Building the Project
If you modify AIDA and want to build it for yourself, here are the dotnet SDK commands you can use!

For windows:
```
dotnet publish AIDA.csproj -c Release --self-contained false
```

For linux:
```
dotnet publish AIDA.csproj -c Release -r linux-x64 --self-contained false
```