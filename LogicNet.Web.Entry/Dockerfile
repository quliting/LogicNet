FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LogicNet.Web.Entry/LogicNet.Web.Entry.csproj", "LogicNet.Web.Entry/"]
COPY ["LogicNet.Web.Core/LogicNet.Web.Core.csproj", "LogicNet.Web.Core/"]
COPY ["LogicNet.Core/LogicNet.Core.csproj", "LogicNet.Core/"]
COPY ["LogicNet.Application/LogicNet.Application.csproj", "LogicNet.Application/"]
RUN dotnet restore "LogicNet.Web.Entry/LogicNet.Web.Entry.csproj"
COPY . .
WORKDIR "/src/LogicNet.Web.Entry"
RUN dotnet build "./LogicNet.Web.Entry.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LogicNet.Web.Entry.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LogicNet.Web.Entry.dll"]
