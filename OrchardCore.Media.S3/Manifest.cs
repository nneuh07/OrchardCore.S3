using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "Microsoft Azure Media",
    Author = "Nils Neuhaus",
    Website = "https://github.com/nneuh07",
    Version = "0.1"
)]

[assembly: Feature(
    Id = "OrchardCore.Media.S3.Storage",
    Name = "S3 Media Storage",
    Description = "Enables support for storing media files in an S3-API compatible Bucket.",
    Dependencies = new[]
    {
        "OrchardCore.Media.Cache"
    },
    Category = "Hosting"
)]