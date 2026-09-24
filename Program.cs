using FireflyHR.API.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QuestPDF.Infrastructure;
using System.Text;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrEmpty(origin)) return false;
            var uri = new Uri(origin);

            return uri.Host == "localhost" ||
                   uri.Host.EndsWith(".devtunnels.ms") ||
                   uri.Host.EndsWith(".cloudfront.net") ||
                   uri.Host.EndsWith(".amazonaws.com") ||
                   uri.Host == "hr.fireflycraftsph.com" ||
                   uri.Host == "staff.fireflycraftsph.com" ||
                   uri.Host.EndsWith(".fireflycraftsph.com");
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// Configure Swagger with JWT Authorization Header Support
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and your generated token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// Enable Swagger globally for development and testing
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Automatically sync all required database columns on every startup / deployment
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();

        dbContext.Database.ExecuteSqlRaw(@"
            DO $$ 
            BEGIN 
                -- 1. Employees table columns
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Employees' and column_name='DailyAllowance') THEN
                    ALTER TABLE ""Employees"" ADD COLUMN ""DailyAllowance"" numeric NOT NULL DEFAULT 0.0;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Employees' and column_name='HasGovernmentDeductions') THEN
                    ALTER TABLE ""Employees"" ADD COLUMN ""HasGovernmentDeductions"" boolean NOT NULL DEFAULT true;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Employees' and column_name='DeductionType') THEN
                    ALTER TABLE ""Employees"" ADD COLUMN ""DeductionType"" text DEFAULT 'Per Pay Period';
                END IF;

                -- 2. PaySlips table columns
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='PaySlips' and column_name='DailyAllowance') THEN
                    ALTER TABLE ""PaySlips"" ADD COLUMN ""DailyAllowance"" numeric NOT NULL DEFAULT 0.0;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='PaySlips' and column_name='SssDeduction') THEN
                    ALTER TABLE ""PaySlips"" ADD COLUMN ""SssDeduction"" numeric NOT NULL DEFAULT 0.0;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='PaySlips' and column_name='PhilHealthDeduction') THEN
                    ALTER TABLE ""PaySlips"" ADD COLUMN ""PhilHealthDeduction"" numeric NOT NULL DEFAULT 0.0;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='PaySlips' and column_name='PagIbigDeduction') THEN
                    ALTER TABLE ""PaySlips"" ADD COLUMN ""PagIbigDeduction"" numeric NOT NULL DEFAULT 0.0;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='PaySlips' and column_name='GovernmentContributions') THEN
                    ALTER TABLE ""PaySlips"" ADD COLUMN ""GovernmentContributions"" numeric NOT NULL DEFAULT 0.0;
                END IF;

            END $$;
        ");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Schema auto-update failed.");
    }
}

app.Run();