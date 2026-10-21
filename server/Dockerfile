FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config HhAutoApplier.slnx ./
COPY src/HhAutoApplier.Core/*.csproj src/HhAutoApplier.Core/
COPY src/HhAutoApplier.Infrastructure/*.csproj src/HhAutoApplier.Infrastructure/
COPY src/HhAutoApplier.Api/*.csproj src/HhAutoApplier.Api/
RUN dotnet restore src/HhAutoApplier.Api/HhAutoApplier.Api.csproj
COPY src/ src/
RUN dotnet publish src/HhAutoApplier.Api/HhAutoApplier.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HhAutoApplier.Api.dll"]
