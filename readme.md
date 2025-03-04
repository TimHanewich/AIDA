# AIDA: AI Desktop Assistant
**AIDA** is a .NET console application that uses the [TimHanewich.AgentFramework](../AgentFramework/) library. It has several capabilities.

To run AIDA, you must first:
1. Install the .NET 9.0 SDK.
2. Download AIDA.exe [here](https://github.com/TimHanewich/TimHanewich.AgentFramework/releases/download/1/AIDA.exe). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

## To build as release as a single self-contained .exe:
```
dotnet publish -c Release --self-contained true
```