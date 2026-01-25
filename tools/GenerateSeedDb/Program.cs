using TermSnap.Services;

Console.WriteLine("=== TermSnap 시드 데이터베이스 생성 도구 ===\n");

// 인자 확인
if (args.Length < 2)
{
    Console.WriteLine("사용법: GenerateSeedDb <json_path> <output_db_path>");
    Console.WriteLine("예시: GenerateSeedDb linux-commands.json seed-history.db");
    return 1;
}

var jsonPath = args[0];
var outputPath = args[1];

// 파일 존재 확인
if (!File.Exists(jsonPath))
{
    Console.WriteLine($"❌ JSON 파일을 찾을 수 없습니다: {jsonPath}");
    return 1;
}

Console.WriteLine($"📄 JSON 파일: {jsonPath}");
Console.WriteLine($"💾 출력 경로: {outputPath}\n");

try
{
    var generator = new SeedDatabaseGenerator();
    var count = await generator.GenerateFromJsonAsync(jsonPath, outputPath);

    Console.WriteLine($"\n✅ 완료! {count}개의 명령어가 데이터베이스에 저장되었습니다.");
    Console.WriteLine($"📍 위치: {Path.GetFullPath(outputPath)}");

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ 오류 발생: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}
