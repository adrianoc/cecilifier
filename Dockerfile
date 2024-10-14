FROM mcr.microsoft.com/dotnet/nightly/sdk:latest as build
COPY . /app/
WORKDIR /app
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/nightly/aspnet:latest as runtime
WORKDIR /app
COPY --from=build /app/out/ ./
ENV DOTNET_ROLL_FORWARD LatestMajor
ENTRYPOINT ["dotnet", "Cecilifier.Web.dll"]