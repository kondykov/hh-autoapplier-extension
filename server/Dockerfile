FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config HhAutoApplier.slnx Directory.Build.props ./
COPY src/HhAutoApplier.Domain/*.csproj src/HhAutoApplier.Domain/
COPY src/HhAutoApplier.Application/*.csproj src/HhAutoApplier.Application/
COPY src/HhAutoApplier.Infrastructure/*.csproj src/HhAutoApplier.Infrastructure/
COPY src/HhAutoApplier.Api/*.csproj src/HhAutoApplier.Api/
RUN dotnet restore src/HhAutoApplier.Api/HhAutoApplier.Api.csproj
COPY src/ src/
# Коммит сборки попадает в GET /api/version: в образе нет .git
ARG GIT_COMMIT=unknown
RUN dotnet publish src/HhAutoApplier.Api/HhAutoApplier.Api.csproj -c Release -o /app --no-restore -p:SourceRevisionId=$GIT_COMMIT

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HhAutoApplier.Api.dll"]
