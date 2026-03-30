var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();

// 🔥 Thêm Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🔥 Bật Swagger (cho mọi môi trường)
app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();

app.MapControllers();

app.Run();