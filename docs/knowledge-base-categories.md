# 리눅스 명령어 지식 베이스 확장 계획

## 추가할 주요 카테고리 (200+ 명령어)

### 1. 텍스트 처리 (15개)
- `sed` - 스트림 편집기
- `awk` - 텍스트 패턴 처리
- `cut` - 필드 추출
- `tr` - 문자 변환
- `sort` - 정렬
- `uniq` - 중복 제거
- `wc` - 단어/줄 수 세기
- `head` - 파일 앞부분 출력
- `tail` - 파일 뒷부분 출력
- `cat` - 파일 연결/출력
- `paste` - 파일 병합
- `join` - 파일 조인
- `diff` - 파일 비교
- `comm` - 공통 줄 추출
- `column` - 컬럼 정렬

### 2. 압축/아카이브 (10개)
- `tar -czf` - tar.gz 압축
- `tar -xzf` - tar.gz 압축 해제
- `zip` - zip 압축
- `unzip` - zip 압축 해제
- `gzip` - gzip 압축
- `gunzip` - gzip 압축 해제
- `bzip2` - bzip2 압축
- `7z` - 7zip 압축
- `rar` - rar 압축
- `unrar` - rar 압축 해제

### 3. 시스템 정보 (20개)
- `uname -a` - 시스템 정보
- `hostname` - 호스트명
- `uptime` - 가동 시간
- `whoami` - 현재 사용자
- `id` - 사용자 ID 정보
- `last` - 로그인 기록
- `w` - 로그인 사용자 목록
- `dmesg` - 커널 메시지
- `lscpu` - CPU 정보
- `lsmem` - 메모리 정보
- `lsblk` - 블록 디바이스 목록
- `lsusb` - USB 장치 목록
- `lspci` - PCI 장치 목록
- `dmidecode` - 하드웨어 정보
- `fdisk -l` - 디스크 파티션 정보
- `mount` - 마운트된 파일시스템
- `sysctl` - 커널 파라미터
- `hostnamectl` - 호스트 정보
- `timedatectl` - 시간/날짜 설정
- `localectl` - 로케일 설정

### 4. 프로세스 & 작업 관리 (15개)
- `ps aux` - 모든 프로세스 목록
- `pgrep` - 프로세스 이름으로 검색
- `pkill` - 프로세스 이름으로 종료
- `killall` - 프로세스 이름으로 모두 종료
- `nice` - 우선순위로 실행
- `renice` - 우선순위 변경
- `jobs` - 백그라운드 작업 목록
- `fg` - 포그라운드로 가져오기
- `bg` - 백그라운드로 보내기
- `screen` - 터미널 멀티플렉서
- `tmux` - 터미널 멀티플렉서
- `nohup` - 연결 끊김 방지 실행
- `disown` - 작업 소유권 해제
- `wait` - 작업 완료 대기
- `at` - 예약 작업

### 5. 크론 & 스케줄링 (5개)
- `crontab -e` - 크론 작업 편집
- `crontab -l` - 크론 작업 목록
- `crontab -r` - 크론 작업 삭제
- `systemctl list-timers` - systemd 타이머 목록
- `anacron` - 주기적 작업 실행

### 6. 사용자 & 권한 관리 (15개)
- `useradd` - 사용자 추가
- `userdel` - 사용자 삭제
- `usermod` - 사용자 수정
- `groupadd` - 그룹 추가
- `groupdel` - 그룹 삭제
- `passwd` - 비밀번호 변경
- `chage` - 비밀번호 만료 관리
- `su` - 사용자 전환
- `sudo -i` - root 셸 시작
- `visudo` - sudoers 파일 편집
- `chmod` - 권한 변경 (상세)
- `chown` - 소유자 변경 (상세)
- `chgrp` - 그룹 변경
- `umask` - 기본 권한 설정
- `getfacl` / `setfacl` - ACL 관리

### 7. 네트워크 심화 (25개)
- `ip addr` - IP 주소 관리
- `ip route` - 라우팅 테이블
- `ifconfig` - 네트워크 인터페이스 (레거시)
- `ping` - 연결 테스트
- `traceroute` - 경로 추적
- `netstat -tuln` - 네트워크 통계
- `ss -tuln` - 소켓 통계 (netstat 대체)
- `nslookup` - DNS 조회
- `dig` - DNS 조회 (상세)
- `host` - DNS 조회 (간단)
- `whois` - 도메인 정보
- `curl` - HTTP 요청 (다양한 옵션)
- `wget` - 파일 다운로드
- `scp` - 보안 파일 복사
- `rsync` - 파일 동기화
- `ssh-keygen` - SSH 키 생성
- `ssh-copy-id` - SSH 키 복사
- `nc` (netcat) - 네트워크 유틸리티
- `tcpdump` - 패킷 캡처
- `iptables` - 방화벽 규칙
- `nft` (nftables) - 방화벽 (최신)
- `ufw` - 간단한 방화벽
- `firewall-cmd` - firewalld 관리
- `nmap` - 포트 스캔
- `mtr` - 네트워크 진단

### 8. 웹서버 (Nginx/Apache) (20개)
- `nginx -t` - 설정 테스트
- `nginx -s reload` - 설정 리로드
- `nginx -s stop` - 중지
- `nginx -V` - 버전 및 모듈 정보
- Apache `httpd -t` - 설정 테스트
- Apache `apachectl graceful` - 재시작
- Apache `a2ensite` - 사이트 활성화
- Apache `a2dissite` - 사이트 비활성화
- Apache `a2enmod` - 모듈 활성화
- Apache `a2dismod` - 모듈 비활성화
- 가상 호스트 설정
- SSL/TLS 인증서 설정
- 리버스 프록시 설정
- 로드 밸런싱 설정
- 접근 제어 설정
- 로그 로테이션
- 에러 페이지 설정
- CORS 설정
- 압축 설정
- 캐시 설정

### 9. 데이터베이스 (15개)
- MySQL/MariaDB
  - `mysql -u root -p` - 접속
  - `mysqldump` - 백업
  - `mysql < backup.sql` - 복원
  - 사용자 생성/권한 부여
  - 데이터베이스 생성/삭제
- PostgreSQL
  - `psql` - 접속
  - `pg_dump` - 백업
  - `pg_restore` - 복원
  - 사용자/DB 관리
- Redis
  - `redis-cli` - 접속
  - 키 조회/설정
- MongoDB
  - `mongosh` - 접속
  - 백업/복원

### 10. Docker 심화 (20개)
- `docker build` - 이미지 빌드
- `docker run` (다양한 옵션)
- `docker exec` - 컨테이너 명령 실행
- `docker-compose up` - 컴포즈 시작
- `docker-compose down` - 컴포즈 중지
- `docker volume` - 볼륨 관리
- `docker network` - 네트워크 관리
- `docker inspect` - 상세 정보
- `docker stats` - 리소스 사용량
- `docker system prune` - 정리
- `docker login` - 레지스트리 로그인
- `docker push` - 이미지 푸시
- `docker pull` - 이미지 풀
- `docker tag` - 이미지 태그
- `docker save` / `load` - 이미지 저장/로드
- `docker export` / `import` - 컨테이너 내보내기
- Dockerfile 베스트 프랙티스
- Multi-stage 빌드
- 컨테이너 네트워킹
- 볼륨 마운트 전략

### 11. Git 심화 (15개)
- `git init` - 저장소 초기화
- `git clone` - 복제
- `git add` - 스테이징
- `git commit` - 커밋
- `git push` - 푸시
- `git pull` - 풀
- `git fetch` - 페치
- `git merge` - 병합
- `git rebase` - 리베이스
- `git checkout` - 체크아웃
- `git branch` - 브랜치 관리
- `git log` - 로그 조회
- `git diff` - 변경사항 비교
- `git stash` - 임시 저장
- `git tag` - 태그 관리

### 12. 파일 시스템 심화 (15개)
- `mkfs` - 파일시스템 생성
- `fsck` - 파일시스템 검사
- `mount` / `umount` - 마운트 관리
- `fstab` - 자동 마운트 설정
- `ln` - 심볼릭 링크 생성
- `readlink` - 심볼릭 링크 해석
- `stat` - 파일 상태 정보
- `file` - 파일 타입 확인
- `sync` - 버퍼 플러시
- `dd` - 디스크 복사 (주의사항 포함)
- `rsync` - 파일 동기화 (상세)
- `inotify` - 파일 변경 감지
- `lsof` - 열린 파일 목록
- `fuser` - 파일 사용 프로세스

### 13. 성능 분석 & 튜닝 (15개)
- `iostat` - I/O 통계
- `vmstat` - 가상 메모리 통계
- `sar` - 시스템 활동 보고
- `perf` - 성능 분석
- `strace` - 시스템 콜 추적
- `ltrace` - 라이브러리 콜 추적
- `gdb` - 디버거
- `valgrind` - 메모리 분석
- `iperf` - 네트워크 성능 측정
- `sysbench` - 벤치마크
- `stress` - 스트레스 테스트
- `mpstat` - CPU 통계
- `pidstat` - 프로세스 통계
- `iotop` - I/O 모니터링
- `nethogs` - 프로세스별 네트워크 사용량

### 14. 로그 관리 (10개)
- `journalctl` - systemd 로그
- `logger` - syslog에 로그 전송
- `logrotate` - 로그 로테이션
- `tail -f` - 실시간 로그
- `less +F` - 실시간 로그 (less)
- `zcat` / `zgrep` - 압축 로그 조회
- `awk` 로그 분석
- `grep` 로그 필터링
- ELK 스택 관련
- Fluentd 관련

### 15. 백업 & 복원 (10개)
- `tar` 백업 전략
- `rsync` 증분 백업
- `dd` 전체 디스크 백업
- `dump` / `restore` - 파일시스템 백업
- `cpio` - 아카이브 도구
- 데이터베이스 백업 (MySQL, PostgreSQL)
- 스냅샷 (LVM, ZFS)
- 원격 백업 (rsync over SSH)
- 백업 스케줄링 (cron)
- 백업 검증

### 16. SSL/TLS & 보안 (15개)
- `openssl` - 인증서 관리
- `certbot` - Let's Encrypt
- `ssh-keygen` - SSH 키 생성
- `gpg` - 암호화/서명
- `fail2ban` - 침입 방지
- `selinux` - 보안 강화
- `apparmor` - 보안 프로파일
- `chroot` - 루트 격리
- `iptables` 보안 규칙
- `ufw` 보안 설정
- 포트 노킹
- VPN 설정 (OpenVPN, WireGuard)
- 2FA 설정
- 암호화된 파일시스템 (LUKS)
- 보안 감사 (Lynis, rkhunter)

### 17. 모니터링 & 알림 (10개)
- Prometheus 설정
- Grafana 설정
- Nagios 설정
- Zabbix 설정
- `monit` - 프로세스 모니터링
- `collectd` - 시스템 통계 수집
- `netdata` - 실시간 모니터링
- `uptimerobot` - 가동 시간 모니터링
- 알림 설정 (이메일, Slack, Discord)
- 메트릭 수집 & 대시보드

### 18. Python/Node.js 환경 (15개)
- Python
  - `python -m venv` - 가상환경
  - `pip install` - 패키지 설치
  - `pip freeze` - 패키지 목록
  - `python -m http.server` - 간단한 HTTP 서버
  - `pyenv` - Python 버전 관리
- Node.js
  - `npm install` - 패키지 설치
  - `npm start` - 앱 시작
  - `npm run build` - 빌드
  - `nvm` - Node 버전 관리
  - `yarn` - 패키지 관리자
  - `pm2` - 프로세스 관리자
  - `nodemon` - 자동 재시작
- 공통
  - 환경 변수 설정
  - 디펜던시 관리
  - 프로덕션 배포

### 19. 환경 변수 & 설정 (5개)
- `export` - 환경 변수 설정
- `env` - 환경 변수 목록
- `printenv` - 특정 변수 출력
- `.bashrc` / `.bash_profile` 설정
- `source` - 설정 파일 리로드

### 20. 기타 유용한 명령어 (20개)
- `xargs` - 파이프라인 확장
- `watch` - 주기적 실행
- `yes` - 자동 응답
- `script` - 터미널 세션 기록
- `history` - 명령어 히스토리
- `alias` - 명령어 별칭
- `time` - 실행 시간 측정
- `date` - 날짜/시간
- `cal` - 달력
- `bc` - 계산기
- `expr` - 수식 계산
- `seq` - 숫자 시퀀스
- `shuf` - 무작위 섞기
- `factor` - 인수분해
- `base64` - Base64 인코딩
- `hexdump` - 16진수 덤프
- `strings` - 바이너리에서 문자열 추출
- `md5sum` / `sha256sum` - 체크섬
- `tree` - 디렉토리 트리
- `cowsay` - 재미있는 출력 😄

## 총 예상 명령어 수: **300+개**

## JSON 파일 구조 제안

```json
{
  "version": "2.0",
  "lastUpdated": "2026-01-24",
  "totalCommands": 300,
  "categories": {
    "패키지 관리": [...],
    "텍스트 처리": [...],
    "압축/아카이브": [...],
    ...
  }
}
```

## 우선순위

### Phase 1 (즉시): 100개
- 기본 시스템 관리
- 패키지 관리
- 네트워크 기본
- 파일 관리
- 프로세스 관리

### Phase 2 (단기): 100개
- Docker/컨테이너
- 웹서버 설정
- 데이터베이스 관리
- Git 심화
- 보안 기본

### Phase 3 (중기): 100개
- 성능 튜닝
- 모니터링/로깅
- 백업/복원
- 고급 네트워킹
- 개발 환경 설정
