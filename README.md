# dcrptd-miner

Multi-platform cyptocurrency miner written mostly in C#.

## Supported algorithms
| Algorithm | Fee |
|:----------|:----|
| sha256bmb | 1% |
| pufferfish2bmb | 1.5% |

## Supported Connection Protocols
| Protocol | Connection prefix |  |
|:---------|:------------------|:-|
| Bamboo Node | bamboo:// | Bamboo solo mining
| Shifupool | shifu:// | Shifupool http://185.215.180.7:4001/ |
| Stratum | stratum+tcp:// |  |

Stratum will be implemented when required

## Development
### Prerequisites
.NET 6 SDK (https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

### Compile and run miner
`dotnet run`

### Compile standalone exe
Windows

`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime win-x64`


Linux

`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime linux-x64`


Mac OS M1

`./m1.sh clang|gcc`
`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime osx-arm64`
