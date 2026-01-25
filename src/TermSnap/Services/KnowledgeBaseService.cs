using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TermSnap.Core;
using TermSnap.Models;

namespace TermSnap.Services;

/// <summary>
/// 미리 준비된 리눅스 명령어 지식을 데이터베이스에 임포트하는 서비스
/// </summary>
public class KnowledgeBaseService
{
    private readonly HistoryDatabaseService _historyDb;

    public KnowledgeBaseService()
    {
        _historyDb = HistoryDatabaseService.Instance;
    }

    /// <summary>
    /// 시드 데이터가 이미 임포트되었는지 확인 (모든 언어)
    /// </summary>
    public bool IsSeedDataImported()
    {
        // 한국어 또는 영어 시드 데이터가 하나라도 있으면 임포트된 것으로 간주
        var koCount = _historyDb.GetHistoryCount(serverProfile: "__SEED_DATA_KO__");
        var enCount = _historyDb.GetHistoryCount(serverProfile: "__SEED_DATA_EN__");
        return koCount > 0 || enCount > 0;
    }

    /// <summary>
    /// 배포된 시드 데이터베이스 파일에서 데이터 임포트
    /// </summary>
    public async Task<int> ImportFromSeedDatabaseAsync(string seedDbPath)
    {
        if (!File.Exists(seedDbPath))
        {
            System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] 시드 DB 파일을 찾을 수 없습니다: {seedDbPath}");
            return 0;
        }

        try
        {
            return await Task.Run(() =>
            {
                var connection = _historyDb.GetConnection();

                // 시드 DB 연결
                using var attachCmd = connection.CreateCommand();
                attachCmd.CommandText = "ATTACH DATABASE @seedDb AS seed";
                attachCmd.Parameters.AddWithValue("@seedDb", seedDbPath);
                attachCmd.ExecuteNonQuery();

                // 시드 데이터 복사 (중복 제외)
                using var copyCmd = connection.CreateCommand();
                copyCmd.CommandText = @"
                    INSERT OR IGNORE INTO command_history
                        (user_input, generated_command, original_command, explanation, output, error,
                         server_profile, is_success, was_edited, executed_at, created_at,
                         embedding_vector, use_count, display_order)
                    SELECT
                        user_input, generated_command, original_command, explanation, output, error,
                        server_profile, is_success, was_edited, executed_at, created_at,
                        embedding_vector, use_count, display_order
                    FROM seed.command_history
                    WHERE server_profile LIKE '__SEED_DATA_%'
                ";
                var importedCount = copyCmd.ExecuteNonQuery();

                // FTS 동기화 (트리거가 자동 처리)

                // 시드 DB 분리
                using var detachCmd = connection.CreateCommand();
                detachCmd.CommandText = "DETACH DATABASE seed";
                detachCmd.ExecuteNonQuery();

                System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] 시드 DB에서 {importedCount}개 항목 임포트 완료");

                return importedCount;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] 시드 DB 임포트 실패: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// JSON 파일에서 지식 베이스 로드 및 데이터베이스에 임포트
    /// </summary>
    public async Task<int> ImportSeedDataAsync(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath))
        {
            throw new FileNotFoundException($"시드 데이터 파일을 찾을 수 없습니다: {jsonFilePath}");
        }

        var json = await File.ReadAllTextAsync(jsonFilePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var seedData = JsonSerializer.Deserialize<SeedDataFile>(json, options);

        if (seedData?.KnowledgeBase == null || seedData.KnowledgeBase.Count == 0)
        {
            return 0;
        }

        int importedCount = 0;
        var embeddingService = AIProviderManager.Instance.CurrentEmbeddingService;

        foreach (var item in seedData.KnowledgeBase)
        {
            try
            {
                // CommandHistory 생성 (언어별로 구분)
                var serverProfile = $"__SEED_DATA_{item.Language.ToUpper()}__";
                var history = new CommandHistory(
                    userInput: item.Question,
                    command: item.Command,
                    serverProfile: serverProfile)
                {
                    Explanation = item.Explanation,
                    IsSuccess = true, // 시드 데이터는 검증된 명령어
                    ExecutedAt = DateTime.Now
                };

                // 임베딩 벡터 생성
                string? embeddingVector = null;
                if (embeddingService != null && embeddingService.IsReady)
                {
                    try
                    {
                        var embedding = await embeddingService.GetEmbeddingAsync(item.Question);
                        embeddingVector = IEmbeddingService.SerializeVector(embedding);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"임베딩 생성 실패 ({item.Question}): {ex.Message}");
                    }
                }

                // DB에 저장
                _historyDb.AddHistory(history, embeddingVector);
                importedCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"항목 임포트 실패 ({item.Question}): {ex.Message}");
            }
        }

        return importedCount;
    }

    /// <summary>
    /// 내장된 기본 지식 베이스 임포트
    /// </summary>
    public async Task<int> ImportDefaultKnowledgeBaseAsync()
    {
        var defaultKnowledge = GetDefaultKnowledgeBase();

        int importedCount = 0;
        var embeddingService = AIProviderManager.Instance.CurrentEmbeddingService;

        foreach (var item in defaultKnowledge)
        {
            try
            {
                // 언어별로 서버 프로필 구분
                var serverProfile = $"__SEED_DATA_{item.Language.ToUpper()}__";
                var history = new CommandHistory(
                    userInput: item.Question,
                    command: item.Command,
                    serverProfile: serverProfile)
                {
                    Explanation = item.Explanation,
                    IsSuccess = true,
                    ExecutedAt = DateTime.Now
                };

                string? embeddingVector = null;
                if (embeddingService != null && embeddingService.IsReady)
                {
                    try
                    {
                        var embedding = await embeddingService.GetEmbeddingAsync(item.Question);
                        embeddingVector = IEmbeddingService.SerializeVector(embedding);
                    }
                    catch { }
                }

                _historyDb.AddHistory(history, embeddingVector);
                importedCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"기본 지식 임포트 실패: {ex.Message}");
            }
        }

        return importedCount;
    }

    /// <summary>
    /// 기본 내장 지식 베이스 (일반적인 리눅스 명령어)
    /// </summary>
    private List<KnowledgeItem> GetDefaultKnowledgeBase()
    {
        return new List<KnowledgeItem>
        {
            // 패키지 관리
            new() { Question = "nginx 설치하는 방법", Command = "sudo apt install nginx -y", Explanation = "nginx 웹서버를 설치합니다. -y 옵션은 자동으로 yes를 선택합니다.", Category = "패키지 관리" },
            new() { Question = "패키지 업데이트 하는 방법", Command = "sudo apt update && sudo apt upgrade -y", Explanation = "패키지 목록을 업데이트하고 설치된 패키지들을 최신 버전으로 업그레이드합니다.", Category = "패키지 관리" },
            new() { Question = "특정 패키지 제거", Command = "sudo apt remove 패키지명", Explanation = "지정한 패키지를 제거합니다. --purge 옵션을 추가하면 설정 파일까지 삭제됩니다.", Category = "패키지 관리" },
            new() { Question = "Docker 설치하는 방법", Command = "curl -fsSL https://get.docker.com | sh", Explanation = "공식 Docker 설치 스크립트를 사용하여 Docker를 설치합니다.", Category = "패키지 관리" },

            // 시스템 모니터링
            new() { Question = "디스크 용량 확인", Command = "df -h", Explanation = "파일시스템별 디스크 사용량을 사람이 읽기 쉬운 형태로 표시합니다.", Category = "시스템 관리" },
            new() { Question = "메모리 사용량 확인", Command = "free -h", Explanation = "시스템의 메모리 사용 현황을 보여줍니다.", Category = "시스템 관리" },
            new() { Question = "실시간 CPU/메모리 모니터링", Command = "htop", Explanation = "대화형 프로세스 뷰어로 실시간 시스템 리소스를 모니터링합니다.", Category = "모니터링" },
            new() { Question = "시스템 리소스 사용량 확인", Command = "top", Explanation = "실행 중인 프로세스와 시스템 리소스 사용량을 실시간으로 보여줍니다.", Category = "모니터링" },
            new() { Question = "특정 폴더 용량 확인", Command = "du -sh 폴더경로", Explanation = "지정한 폴더의 총 사용 용량을 사람이 읽기 쉬운 형태로 표시합니다.", Category = "시스템 관리" },

            // 네트워크
            new() { Question = "특정 포트를 사용하는 프로세스 찾기", Command = "sudo lsof -i :포트번호", Explanation = "지정한 포트를 사용 중인 프로세스를 찾습니다.", Category = "네트워크" },
            new() { Question = "열린 포트 확인", Command = "sudo netstat -tuln", Explanation = "현재 열려있는 모든 TCP/UDP 포트를 표시합니다.", Category = "네트워크" },
            new() { Question = "방화벽 상태 확인", Command = "sudo ufw status", Explanation = "UFW 방화벽의 현재 상태와 규칙을 표시합니다.", Category = "네트워크" },
            new() { Question = "특정 포트 방화벽 열기", Command = "sudo ufw allow 포트번호", Explanation = "지정한 포트에 대한 인바운드 연결을 허용합니다.", Category = "네트워크" },
            new() { Question = "공인 IP 주소 확인", Command = "curl ifconfig.me", Explanation = "서버의 공인 IP 주소를 확인합니다.", Category = "네트워크" },

            // 프로세스 관리
            new() { Question = "특정 프로세스 종료", Command = "sudo kill -9 PID", Explanation = "지정한 프로세스 ID를 강제로 종료합니다.", Category = "프로세스 관리" },
            new() { Question = "프로세스 이름으로 찾기", Command = "ps aux | grep 프로세스명", Explanation = "실행 중인 프로세스 중에서 특정 이름을 포함한 프로세스를 찾습니다.", Category = "프로세스 관리" },
            new() { Question = "백그라운드 실행", Command = "nohup 명령어 &", Explanation = "명령어를 백그라운드에서 실행하고 터미널 종료 후에도 계속 실행되도록 합니다.", Category = "프로세스 관리" },

            // 서비스 관리
            new() { Question = "nginx 시작하는 방법", Command = "sudo systemctl start nginx", Explanation = "nginx 서비스를 시작합니다.", Category = "서비스 관리" },
            new() { Question = "nginx 중지하는 방법", Command = "sudo systemctl stop nginx", Explanation = "nginx 서비스를 중지합니다.", Category = "서비스 관리" },
            new() { Question = "nginx 재시작하는 방법", Command = "sudo systemctl restart nginx", Explanation = "nginx 서비스를 재시작합니다. 설정 변경 후 적용할 때 사용합니다.", Category = "서비스 관리" },
            new() { Question = "서비스 상태 확인", Command = "sudo systemctl status 서비스명", Explanation = "지정한 서비스의 현재 상태와 최근 로그를 확인합니다.", Category = "서비스 관리" },
            new() { Question = "부팅 시 자동 시작 설정", Command = "sudo systemctl enable 서비스명", Explanation = "시스템 부팅 시 서비스가 자동으로 시작되도록 설정합니다.", Category = "서비스 관리" },

            // 파일/폴더 관리
            new() { Question = "파일 검색하는 방법", Command = "find /경로 -name '파일명'", Explanation = "지정한 경로에서 파일명으로 파일을 검색합니다.", Category = "파일 관리" },
            new() { Question = "텍스트 파일 내용 검색", Command = "grep -r '검색어' /경로", Explanation = "지정한 경로의 모든 파일에서 특정 텍스트를 검색합니다.", Category = "파일 관리" },
            new() { Question = "파일 권한 변경", Command = "chmod 755 파일명", Explanation = "파일의 권한을 변경합니다. 755는 소유자는 읽기/쓰기/실행, 그룹과 기타는 읽기/실행 권한입니다.", Category = "파일 관리" },
            new() { Question = "파일 소유자 변경", Command = "sudo chown 사용자:그룹 파일명", Explanation = "파일의 소유자와 그룹을 변경합니다.", Category = "파일 관리" },
            new() { Question = "압축 파일 압축 해제", Command = "tar -xzf 파일명.tar.gz", Explanation = "tar.gz 압축 파일을 현재 디렉토리에 압축 해제합니다.", Category = "파일 관리" },

            // 로그 확인
            new() { Question = "실시간 로그 보기", Command = "tail -f /var/log/syslog", Explanation = "시스템 로그를 실시간으로 모니터링합니다.", Category = "로그 관리" },
            new() { Question = "nginx 에러 로그 확인", Command = "sudo tail -f /var/log/nginx/error.log", Explanation = "nginx 에러 로그를 실시간으로 확인합니다.", Category = "로그 관리" },
            new() { Question = "마지막 100줄 로그 보기", Command = "tail -n 100 로그파일경로", Explanation = "로그 파일의 마지막 100줄을 표시합니다.", Category = "로그 관리" },

            // Git
            new() { Question = "Git 저장소 복제", Command = "git clone 저장소URL", Explanation = "원격 Git 저장소를 로컬로 복제합니다.", Category = "Git" },
            new() { Question = "Git 브랜치 확인", Command = "git branch -a", Explanation = "로컬과 원격의 모든 브랜치를 표시합니다.", Category = "Git" },
            new() { Question = "Git 변경사항 커밋", Command = "git add . && git commit -m '커밋 메시지'", Explanation = "모든 변경사항을 스테이징하고 커밋합니다.", Category = "Git" },

            // Docker
            new() { Question = "Docker 컨테이너 목록 확인", Command = "docker ps -a", Explanation = "실행 중이거나 중지된 모든 Docker 컨테이너를 표시합니다.", Category = "Docker" },
            new() { Question = "Docker 이미지 목록 확인", Command = "docker images", Explanation = "로컬에 저장된 Docker 이미지 목록을 표시합니다.", Category = "Docker" },
            new() { Question = "Docker 컨테이너 시작", Command = "docker start 컨테이너명", Explanation = "중지된 Docker 컨테이너를 시작합니다.", Category = "Docker" },
            new() { Question = "Docker 컨테이너 중지", Command = "docker stop 컨테이너명", Explanation = "실행 중인 Docker 컨테이너를 중지합니다.", Category = "Docker" },
            new() { Question = "Docker 로그 확인", Command = "docker logs -f 컨테이너명", Explanation = "Docker 컨테이너의 로그를 실시간으로 확인합니다.", Category = "Docker" },

            // 사용자 관리
            new() { Question = "새 사용자 추가", Command = "sudo adduser 사용자명", Explanation = "새로운 사용자 계정을 생성합니다.", Category = "사용자 관리" },
            new() { Question = "사용자에게 sudo 권한 부여", Command = "sudo usermod -aG sudo 사용자명", Explanation = "지정한 사용자를 sudo 그룹에 추가하여 관리자 권한을 부여합니다.", Category = "사용자 관리" },
            new() { Question = "현재 로그인한 사용자 확인", Command = "whoami", Explanation = "현재 로그인한 사용자의 이름을 표시합니다.", Category = "사용자 관리" },
        };
    }
}

/// <summary>
/// 시드 데이터 파일 구조
/// </summary>
public class SeedDataFile
{
    public List<KnowledgeItem> KnowledgeBase { get; set; } = new();
}

/// <summary>
/// 지식 항목
/// </summary>
public class KnowledgeItem
{
    public string Question { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string Language { get; set; } = "ko"; // ko, en, etc.
}
