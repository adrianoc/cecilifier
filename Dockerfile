FROM microsoft/dotnet:2.2-sdk-alpine3.8 as build
COPY . /app/
WORKDIR /app
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM microsoft/dotnet:2.2-aspnetcore-runtime-alpine3.8 as runtime
WORKDIR /app
COPY --from=build /app/Cecilifier.Web/out/ ./
ENTRYPOINT ["dotnet", "Cecilifier.Web.dll"]