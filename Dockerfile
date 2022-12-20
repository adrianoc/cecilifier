FROM  mcr.microsoft.com/dotnet/nightly/sdk:8.0.100-alpha.1-alpine3.17 as build
COPY . /app/
WORKDIR /app
RUN dotnet restore
RUN dotnet publish -c Release

FROM  mcr.microsoft.com/dotnet/nightly/sdk:8.0.100-alpha.1-alpine3.17 as runtime
WORKDIR /app
COPY --from=build /app/Cecilifier.Web/bin/Release/net6.0/publish/ ./
COPY --from=build /app/Cecilifier.Web/wwwroot/lib/node_modules/monaco-editor/ wwwroot/lib/node_modules/monaco-editor/
ENTRYPOINT ["dotnet", "--roll-forward", "LatestMajor", "Cecilifier.Web.dll"]

#sudo docker build . -t cecilifier/8.0
#sudo docker run -d -p 5000:5000 cecilifier/8.0