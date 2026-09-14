FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Client/SmartX.Client.csproj", "SmartX.Client/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Client/SmartX.Client.csproj"
COPY . .
WORKDIR "/src/SmartX.Client"
RUN dotnet publish "SmartX.Client.csproj" -c Release -o /app/publish

# Swap in the Docker-specific API URL instead of the local-dev one
COPY SmartX.Client/appsettings.Docker.json /app/publish/wwwroot/appsettings.json

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY SmartX.Client/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80