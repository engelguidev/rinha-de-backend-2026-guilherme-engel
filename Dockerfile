FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Directory.Build.props ./
COPY src/Rinha.FraudDetection.Domain/Rinha.FraudDetection.Domain.csproj src/Rinha.FraudDetection.Domain/
COPY src/Rinha.FraudDetection.Application/Rinha.FraudDetection.Application.csproj src/Rinha.FraudDetection.Application/
COPY src/Rinha.FraudDetection.Infrastructure/Rinha.FraudDetection.Infrastructure.csproj src/Rinha.FraudDetection.Infrastructure/
COPY src/Rinha.FraudDetection.Presentation/Rinha.FraudDetection.Presentation.csproj src/Rinha.FraudDetection.Presentation/

RUN dotnet restore src/Rinha.FraudDetection.Presentation/Rinha.FraudDetection.Presentation.csproj

COPY src/ src/

RUN dotnet publish src/Rinha.FraudDetection.Presentation/Rinha.FraudDetection.Presentation.csproj \
    -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:9999
ENV DOTNET_gcServer=1
ENV DOTNET_GCHeapHardLimit=200000000

COPY --from=build /app/publish ./
COPY resources/ /app/resources/
COPY data/ /app/data/

ENTRYPOINT ["dotnet", "Rinha.FraudDetection.Presentation.dll"]
