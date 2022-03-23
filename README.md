# dcrptd-miner

Multi-platform cyptocurrency miner written mostly in C#.

Currently supports only Shifupool protocol (http://185.215.180.7:4001/) which is used to mine Bamboo (https://github.com/mr-pandabear/bamboo) coin. 
0% fee.

## Prerequisites
.NET 6 SDK (https://dotnet.microsoft.com/en-us/download/dotnet/6.0)

## Compile and run miner
`dotnet run`

## Compile standalone exe
Windows

`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime win-x64`


Linux

`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime linux-x64`


Mac OSX

`dotnet publish -c Release -p:PublishSingleFile=true --self-contained --runtime osx-x64`
