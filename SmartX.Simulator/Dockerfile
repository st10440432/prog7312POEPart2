FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["SmartX.Simulator/SmartX.Simulator.csproj", "SmartX.Simulator/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]

RUN dotnet restore "SmartX.Simulator/SmartX.Simulator.csproj"

COPY . .

RUN dotnet publish "SmartX.Simulator/SmartX.Simulator.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SmartX.Simulator.dll"]
