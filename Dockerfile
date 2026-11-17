# Plateia · imagem da aplicação (.NET 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Plateia.sln ./
COPY src/Plateia.Domain/Plateia.Domain.csproj src/Plateia.Domain/
COPY src/Plateia.Application/Plateia.Application.csproj src/Plateia.Application/
COPY src/Plateia.Infrastructure/Plateia.Infrastructure.csproj src/Plateia.Infrastructure/
COPY src/Plateia.Web/Plateia.Web.csproj src/Plateia.Web/
RUN dotnet restore src/Plateia.Web/Plateia.Web.csproj
COPY src/ src/
RUN dotnet publish src/Plateia.Web/Plateia.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    Uploads__Pasta=/dados/uploads
EXPOSE 8080
ENTRYPOINT ["dotnet", "Plateia.Web.dll"]
