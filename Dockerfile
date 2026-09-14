FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["FireflyHR.API.csproj", "./"]
RUN dotnet restore "FireflyHR.API.csproj"
COPY . .
RUN dotnet publish "FireflyHR.API.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FireflyHR.API.dll"]