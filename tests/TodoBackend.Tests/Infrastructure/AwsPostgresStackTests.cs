// ABOUTME: Guards the OpenTofu stack so an AWS Postgres database stays defined and discoverable.
// ABOUTME: Enforces key resource names and engine settings to keep docs and IaC aligned.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TodoBackend.Tests.Infrastructure;

public class AwsPostgresStackTests
{
    private static readonly Lazy<string> RepoRoot = new(LocateRepoRoot);

    [Fact]
    public void TerraformStackDefinesPostgresInstance()
    {
        var mainTfPath = Path.Combine(RepoRoot.Value, "infra", "aws", "postgres", "main.tf");
        Assert.True(File.Exists(mainTfPath), $"OpenTofu config missing: {mainTfPath}");

        var contents = File.ReadAllText(mainTfPath);
        Assert.Contains("resource \"aws_db_instance\" \"todo_backend_postgres\"", contents);
        Assert.Matches(@"engine\s*=\s*""postgres""", contents);
        Assert.Matches(@"db_subnet_group_name\s*=\s*aws_db_subnet_group\.todo_backend_postgres\.name", contents);
        Assert.Matches(@"vpc_security_group_ids\s*=\s*\[aws_security_group\.todo_backend_postgres\.id\]", contents);
        Assert.True(
            Regex.IsMatch(contents, @"provider\s+""aws""\s*{[^}]*profile\s*=\s*var\.aws_profile", RegexOptions.Singleline),
            "AWS provider block should expose the aws_profile variable."
        );
    }

    private static string LocateRepoRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "TodoBackend.sln")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to find repository root with TodoBackend.sln");
    }
}
