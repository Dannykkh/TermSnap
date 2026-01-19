# 아이콘 변환 가이드

## 온라인 변환 (가장 간단)

1. **CloudConvert** (https://cloudconvert.com/svg-to-ico)
   - `icon.svg` 파일 업로드
   - 출력 형식: ICO
   - 크기 설정: 256x256, 128x128, 64x64, 48x48, 32x32, 16x16 (모두 선택)
   - 변환 후 `icon.ico`로 저장

2. **또는 convertio.co** (https://convertio.co/kr/svg-ico/)
   - 더 빠른 대안

## ImageMagick 사용 (로컬 변환)

```powershell
# ImageMagick 설치 (Chocolatey 사용)
choco install imagemagick

# SVG를 여러 크기의 ICO로 변환
magick convert -density 256x256 -background transparent icon.svg -define icon:auto-resize=256,128,64,48,32,16 icon.ico
```

## GIMP 사용 (무료 소프트웨어)

1. GIMP에서 `icon.svg` 열기
2. 이미지 → 이미지 크기 조정 → 256x256
3. 파일 → 내보내기 → `icon.ico`
4. 모든 크기 포함 옵션 선택

## 변환 완료 후

변환된 `icon.ico` 파일을:
- `assets/icon.ico`에 저장
- 프로젝트 설정에서 아이콘으로 지정
