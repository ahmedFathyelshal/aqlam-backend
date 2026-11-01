using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using static AqlamAcademy.Api.IntegrationTests.CourseTestHelpers;

namespace AqlamAcademy.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CurriculumSyncTests(ApiFactory factory)
{
    [Fact]
    public async Task Saving_again_keeps_ids_reorders_moves_and_soft_deletes()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "sync");
        var courseId = await CreateDraftAsync(teacher, org);

        var first = await SyncAsync(teacher, org, courseId, 0,
        [
            new { title = "Chapter A", lessons = new[] { Lesson("A1", resources: [new { displayName = "Notes", resourceUrl = "https://files.example/a1.pdf", type = "File" }]), Lesson("A2") } },
            new { title = "Chapter B", lessons = new[] { Lesson("B1") } }
        ]);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var v1 = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(1, v1.GetProperty("curriculumVersion").GetInt32());

        var chapterA = v1.GetProperty("chapters")[0];
        var chapterB = v1.GetProperty("chapters")[1];
        var a1 = chapterA.GetProperty("lessons")[0];
        var a2Id = chapterA.GetProperty("lessons")[1].GetProperty("id").GetString()!;
        var b1Id = chapterB.GetProperty("lessons")[0].GetProperty("id").GetString()!;
        var a1Id = a1.GetProperty("id").GetString()!;
        var resourceId = a1.GetProperty("resources")[0].GetProperty("id").GetString()!;
        var chapterAId = chapterA.GetProperty("id").GetString()!;
        var chapterBId = chapterB.GetProperty("id").GetString()!;

        // Rename A, reverse its lessons, move B1 into A, add a new lesson, drop chapter B.
        var second = await SyncAsync(teacher, org, courseId, 1,
        [
            new
            {
                id = chapterAId,
                title = "Chapter A (renamed)",
                lessons = new[]
                {
                    Lesson("A2", a2Id),
                    Lesson("A1", a1Id, resources: [new { id = resourceId, displayName = "Notes v2", resourceUrl = "https://files.example/a1.pdf", type = "File" }]),
                    Lesson("B1 moved", b1Id),
                    Lesson("New lesson")
                }
            }
        ]);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var v2 = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        Assert.Equal(2, v2.GetProperty("curriculumVersion").GetInt32());
        var chapters = v2.GetProperty("chapters");
        Assert.Equal(1, chapters.GetArrayLength());
        Assert.Equal(chapterAId, chapters[0].GetProperty("id").GetString());
        Assert.Equal("Chapter A (renamed)", chapters[0].GetProperty("title").GetString());

        var lessons = chapters[0].GetProperty("lessons").EnumerateArray().ToList();
        Assert.Equal([a2Id, a1Id, b1Id], lessons.Take(3).Select(l => l.GetProperty("id").GetString()));
        Assert.Equal("New lesson", lessons[3].GetProperty("title").GetString());
        Assert.Equal(resourceId, lessons[1].GetProperty("resources")[0].GetProperty("id").GetString());
        Assert.Equal("Notes v2", lessons[1].GetProperty("resources")[0].GetProperty("displayName").GetString());

        // Removed chapter is soft-deleted, not physically deleted.
        Assert.True(await IsSoftDeletedAsync("CourseChapters", chapterBId));
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_409_and_changes_nothing()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "stale");
        var courseId = await CreateDraftAsync(teacher, org);
        (await SyncAsync(teacher, org, courseId, 0, [new { title = "Chapter 1", lessons = Array.Empty<object>() }])).EnsureSuccessStatusCode();

        // A second editor still holding version 0 tries to save.
        var stale = await SyncAsync(teacher, org, courseId, 0, [new { title = "Overwrite", lessons = Array.Empty<object>() }]);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Chapter 1", (await GetDraftAsync(teacher, org, courseId)).GetProperty("chapters")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Chapter_without_lessons_list_keeps_its_lessons()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "keep");
        var courseId = await CreateDraftAsync(teacher, org);
        var created = (await (await SyncAsync(teacher, org, courseId, 0, [new { title = "Unit", lessons = new[] { Lesson("L1"), Lesson("L2") } }]))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var chapterId = created.GetProperty("chapters")[0].GetProperty("id").GetString();

        // Skeleton step: only chapter titles are sent.
        (await SyncAsync(teacher, org, courseId, 1, [new { id = chapterId, title = "Unit (renamed)" }])).EnsureSuccessStatusCode();

        var chapter = (await GetDraftAsync(teacher, org, courseId)).GetProperty("chapters")[0];
        Assert.Equal("Unit (renamed)", chapter.GetProperty("title").GetString());
        Assert.Equal(2, chapter.GetProperty("lessons").GetArrayLength());
    }

    [Fact]
    public async Task Ids_from_another_course_and_invalid_types_are_rejected()
    {
        var (teacher, org) = await TeacherWithPersonalOrgAsync(factory, "foreign");
        var courseA = await CreateDraftAsync(teacher, org, "A");
        var courseB = await CreateDraftAsync(teacher, org, "B");
        var lessonOfA = (await (await SyncAsync(teacher, org, courseA, 0, [new { title = "Unit", lessons = new[] { Lesson("L1") } }]))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("chapters")[0].GetProperty("lessons")[0].GetProperty("id").GetString();

        var foreign = await SyncAsync(teacher, org, courseB, 0, [new { title = "Steal", lessons = new[] { Lesson("Stolen", lessonOfA) } }]);
        var badType = await SyncAsync(teacher, org, courseB, 0, [new { title = "Unit", lessons = new[] { Lesson("X", type: "Podcast") } }]);

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        Assert.Equal(0, (await GetDraftAsync(teacher, org, courseB)).GetProperty("chapters").GetArrayLength());
    }

    private async Task<bool> IsSoftDeletedAsync(string table, string id)
    {
        await using var connection = new NpgsqlConnection(factory.SharedConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT \"IsDeleted\" FROM \"Courses\".\"{table}\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", Guid.Parse(id));
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
