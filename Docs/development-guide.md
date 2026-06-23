# saleserp Development Guide

이 문서는 ECNESOFT Field Sales 프로젝트를 유지보수하거나 확장하는 개발자를 위한 개발 문서입니다.

## 1. 프로젝트 개요

`saleserp`는 호주 Sydney 시장을 기준으로 만든 웹 기반 B2B 필드 세일즈 관리 시스템입니다.

주요 기능:

- 관리자 및 영업사원 로그인
- HTTP-only Cookie 및 Bearer JWT 인증
- 역할 기반 권한 제어: `ADMIN`, `SALES`
- Google Maps 기반 고객 위치 표시
- 고객 Type 필터링: `ACTIVE`, `TERMINATION`, `CLOSED`, `PROSPECT`, `OWNERSHIP`
- 고객 직접 등록 및 수정
- Latitude, Longitude 직접 입력 기반 Google Map 매핑
- 고객 Note 표시
- Happy Visit 고객 그룹 관리
- Dashboard 게시판 작성, 조회, 수정, 삭제
- Customer XML 템플릿 Export 및 XML Import
- ABN CSV/XLSX Bulk Import API
- 사진 업로드 기반 Sales Note 저장
- Haversine 기반 반경 내 Prospect 추천 API
- Postcode/Suburb별 침투율 Dashboard API

## 2. 기술 스택

- Backend: ASP.NET Core 8 Minimal API
- Language: C# 12, nullable enabled
- Frontend: HTML, CSS, Vanilla JavaScript
- Auth: Cookie Authentication, custom HMAC JWT Authentication
- Map: Google Maps JavaScript API, internal SVG map fallback
- Storage: 현재 In-memory repository
- Schema reference: SQLite/SQL Server 스타일 SQL 문서 제공
- Import: CSV/XLSX parser, XML Spreadsheet import on frontend

## 3. 폴더 구조

```text
EcnesoftFieldSales/
  Auth/
    JwtAuthenticationHandler.cs
  Docs/
    controller-example.md
    development-guide.md
    frontend-google-maps.md
    schema.sql
  Domain/
    Dtos.cs
    Entities.cs
  Infrastructure/
    InMemorySalesRepository.cs
  Services/
    AbnServices.cs
    FileStorageService.cs
    ImportParser.cs
    JwtTokenService.cs
    PasswordHasher.cs
  wwwroot/
    index.html
    app.js
    app.css
    sample-import.csv
    assets/
    uploads/
  Program.cs
  appsettings.json
  README.md
```

핵심 파일:

- `Program.cs`: API endpoint, middleware, authentication, authorization 설정
- `Domain/Entities.cs`: 핵심 도메인 enum 및 entity
- `Domain/Dtos.cs`: API request/response DTO
- `Infrastructure/InMemorySalesRepository.cs`: 현재 데이터 저장소
- `wwwroot/app.js`: 대부분의 프론트엔드 상태 관리, API 호출, Google Map, XML Import/Export 로직
- `wwwroot/index.html`: 앱 화면 구조
- `wwwroot/app.css`: UI 스타일

## 4. 로컬 실행

필수 조건:

- .NET 8 SDK
- 브라우저
- Google Maps API Key는 선택 사항. 없으면 내부 fallback map을 사용합니다.

실행:

```powershell
cd C:\Users\user\OneDrive\Desktop\EcnesoftFieldSales
$env:DOTNET_CLI_HOME='C:\Users\user\OneDrive\Desktop\.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet run --urls http://127.0.0.1:5127
```

Google Maps 사용 시:

```powershell
$env:GoogleMaps__ApiKey="YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY"
$env:GoogleMaps__MapId="DEMO_MAP_ID"
dotnet run --urls http://127.0.0.1:5127
```

접속:

```text
http://127.0.0.1:5127/
```

기본 로그인:

```text
ID: Ecnesoft
PW: Ecnesoft
```

## 5. 설정

`appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "EcnesoftFieldSales",
    "Audience": "EcnesoftSalesWeb",
    "SigningKey": "CHANGE_ME_TO_A_64_BYTE_SECRET_IN_PRODUCTION_2026",
    "AccessTokenMinutes": 480
  },
  "Security": {
    "MaxUploadBytes": 5242880
  },
  "GoogleMaps": {
    "ApiKey": "YOUR_BROWSER_RESTRICTED_GOOGLE_MAPS_KEY",
    "MapId": "DEMO_MAP_ID"
  }
}
```

주의:

- 실제 Google Maps API Key는 GitHub에 커밋하지 않습니다.
- 운영 환경에서는 `Jwt:SigningKey`를 반드시 강한 secret으로 교체해야 합니다.
- 운영 환경에서는 HTTPS와 Secure Cookie가 필요합니다.

## 6. 인증과 권한

인증 방식:

- 로그인 성공 시 Cookie 인증과 JWT token을 함께 지원합니다.
- 프론트엔드는 `localStorage`에 JWT를 저장하고 API 요청 시 `Authorization: Bearer {token}`을 붙입니다.
- Cookie는 HTTP-only로 설정되어 브라우저 기반 인증도 가능합니다.

권한 정책:

- `SalesOrAdmin`: `ADMIN`, `SALES`
- `AdminOnly`: `ADMIN`

역할:

- `ADMIN`: 관리자, 전체 고객 및 관리자 API 접근 가능
- `SALES`: 영업사원, 일반 고객 관리 및 영업 기능 접근 가능

## 7. 도메인 모델

주요 enum:

```text
UserRole:
  ADMIN, SALES

CustomerLifecycleType:
  ACTIVE, TERMINATION, CLOSED, PROSPECT, OWNERSHIP

CompetitorType:
  KPOS, ORDERNOW, QONUS, SQUARE, ETC
```

주요 entity:

- `UserAccount`: 로그인 계정
- `Customer`: 고객 및 매장 정보
- `ClientGroup`: 고객 그룹
- `SalesNote`: 현장 방문 기록 및 이미지
- `HappyVisitGroup`: Happy Visit 그룹
- `DashboardPost`: Dashboard 게시판 글

## 8. Backend API

공통:

- 인증 필요 API는 `Authorization: Bearer {token}` 또는 인증 Cookie를 사용합니다.
- JSON API는 기본적으로 `application/json`을 사용합니다.
- 파일 업로드는 `multipart/form-data`를 사용합니다.

### Auth

| Method | Path | Role | Description |
|---|---|---|---|
| POST | `/api/auth/login` | Public | 로그인 |
| GET | `/api/auth/me` | ADMIN, SALES | 현재 사용자 |
| POST | `/api/auth/logout` | ADMIN, SALES | 로그아웃 |

### Customers

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/customers` | ADMIN, SALES | 고객 목록 |
| POST | `/api/customers` | ADMIN, SALES | 고객 생성 |
| PUT | `/api/customers/{id}` | ADMIN, SALES | 고객 수정 |
| PATCH | `/api/customers/{id}/coordinates` | ADMIN, SALES | 좌표 수정 |

`POST /api/customers` 예시:

```json
{
  "companyName": "Sample Store",
  "abn": "",
  "address": "14/20-30 Stubbs St",
  "city": "Silverwater",
  "state": "NSW",
  "postcode": "2128",
  "phone": "",
  "email": "",
  "latitude": -33.8353852,
  "longitude": 151.0398518,
  "type": "PROSPECT",
  "terminationDate": null,
  "terminationReason": null,
  "competitor": "KPOS",
  "generalNote": "Customer memo",
  "groupId": null,
  "assignedUserId": 2
}
```

### Sales Notes

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/sales-notes?customerId={id}` | ADMIN, SALES | 방문 기록 조회 |
| POST | `/api/customers/{id}/notes` | ADMIN, SALES | 방문 기록 및 이미지 업로드 |

### Happy Visit

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/happy-visits` | ADMIN, SALES | 그룹 목록 |
| POST | `/api/happy-visits` | ADMIN, SALES | 그룹 생성 |
| PUT | `/api/happy-visits/{id}` | ADMIN, SALES | 그룹 수정 |

### Dashboard Board

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/dashboard/posts` | ADMIN, SALES | 게시글 목록 |
| GET | `/api/dashboard/posts/{id}` | ADMIN, SALES | 게시글 상세 |
| POST | `/api/dashboard/posts` | ADMIN, SALES | 게시글 생성 |
| PUT | `/api/dashboard/posts/{id}` | ADMIN, SALES | 게시글 수정 |
| DELETE | `/api/dashboard/posts/{id}` | ADMIN, SALES | 게시글 삭제 |

### Admin

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/users/sales` | ADMIN | 영업사원 목록 |
| POST | `/api/admin/import` | ADMIN | ABN CSV/XLSX Bulk Import |
| GET | `/api/admin/dashboard/penetration` | ADMIN | 침투율 Dashboard |

### Recommendations

| Method | Path | Role | Description |
|---|---|---|---|
| GET | `/api/recommendations?latitude={lat}&longitude={lng}&radiusKm={km}` | ADMIN, SALES | 반경 내 Prospect 추천 |

## 9. Frontend 구조

프론트엔드는 `wwwroot/app.js`에서 단일 상태 객체를 기준으로 동작합니다.

주요 상태:

```js
state = {
  token,
  user,
  customers,
  happyGroups,
  posts,
  selectedCustomerId,
  filters,
  customerFilters,
  google
}
```

주요 화면:

- `Map`: Google Map 및 고객 필터
- `Happy Visit`: 고객 그룹 관리
- `Dashboard`: 게시판
- `Customer`: 고객 리스트, XML Export/Import

주요 프론트 함수:

- `login`, `logout`, `api`
- `renderAll`
- `renderGoogleMarkers`
- `openNewCustomerModal`
- `createCustomer`
- `openEditCustomerModal`
- `exportCustomerXmlTemplate`
- `importCustomerXml`
- `parseCustomerImportXml`

## 10. Google Maps 동작

초기 Google Map:

- 기본 중심: Sydney
- 기본 줌: `z11`
- Filter 선택 시 Sydney `z11`로 리셋
- Reset 버튼도 Sydney `z11`로 리셋

Marker 색상:

```text
ACTIVE: green
TERMINATION: orange
CLOSED: red
PROSPECT: blue
OWNERSHIP: teal
```

Competitor 필터 색상:

```text
KPOS: yellow, K
ORDERNOW: purple, O
QONUS: sky blue, Q
SQUARE: beige, S
ETC: lime, E
```

고객 등록:

- Latitude, Longitude는 사용자가 직접 입력합니다.
- 입력된 좌표로 Google Map preview marker가 이동합니다.
- 저장 시 입력 좌표가 고객 좌표로 저장됩니다.

## 11. Customer XML Export / Import

위치:

```text
Customer 화면 상단
  Export Template
  Import XML
```

템플릿 형식:

- Excel에서 열 수 있는 XML Spreadsheet 형식
- Sheet name: `Customer Import`
- 1행은 헤더
- 2행부터 고객 데이터 입력

헤더:

```text
Company | Type | Termination Date | Termination Reason | Competitor | ABN | Address | Suburb | State | Postcode | Latitude | Longitude | Note
```

필수값:

```text
Company
Type
Address
Suburb
State
Postcode
Latitude
Longitude
```

조건부 필수:

```text
Type = TERMINATION 인 경우:
  Termination Date
  Termination Reason
```

허용 Type:

```text
ACTIVE
TERMINATION
CLOSED
PROSPECT
OWNERSHIP
```

허용 Competitor:

```text
KPOS
ORDERNOW
QONUS
SQUARE
ETC
```

Import 동작:

- XML 파일을 브라우저에서 읽습니다.
- 각 row를 `/api/customers`로 등록합니다.
- 성공한 고객의 Type 필터가 Customer 화면에서 자동 체크됩니다.
- 일부 row가 실패해도 나머지 row는 계속 import합니다.
- 오류는 status message에 최대 3개까지 요약 표시합니다.

주의:

- 사진 파일은 XML에 포함하지 않습니다.
- `Choose File` 업로드는 New modal 또는 Sales Note API에서 처리합니다.

## 12. ABN Bulk Import

관리자 전용 API:

```text
POST /api/admin/import
Content-Type: multipart/form-data
file: CSV 또는 XLSX
```

현재 구현:

- `Services/ImportParser.cs`가 CSV/XLSX를 읽습니다.
- `MockAbrLookupClient`가 ABR lookup을 mock 처리합니다.
- ABN checksum validation을 수행합니다.
- ABN 기준 upsert를 수행합니다.

운영 전환 시:

- 실제 ABR Lookup API client로 교체
- API key 보관 방식 변경
- import 실패 row를 별도 파일 또는 DB table로 저장하는 구조 권장

## 13. 파일 업로드

파일 업로드 서비스:

```text
Services/FileStorageService.cs
```

저장 위치:

```text
wwwroot/uploads/sales-notes/yyyyMMdd/
```

제한:

- `Security:MaxUploadBytes`
- 기본 5MB
- 허용 확장자는 코드에서 이미지 MIME 기준으로 처리합니다.

`.gitignore`에서 `wwwroot/uploads/`는 제외되어 있습니다.

## 14. 데이터 저장소

현재:

- `InMemorySalesRepository`
- 앱 재시작 시 데이터 초기화

운영 권장:

- EF Core 또는 ADO.NET 기반 DB 저장소로 교체
- `Docs/schema.sql` 기준으로 SQLite 또는 SQL Server 테이블 생성
- repository interface는 유지하고 구현체만 교체하는 방식 권장

교체 대상:

```text
builder.Services.AddSingleton<ISalesRepository, InMemorySalesRepository>();
```

예:

```csharp
builder.Services.AddScoped<ISalesRepository, SqlSalesRepository>();
```

## 15. 빌드와 검증

문법 확인:

```powershell
node --check wwwroot\app.js
```

.NET 빌드:

```powershell
dotnet build
```

실행:

```powershell
dotnet run --urls http://127.0.0.1:5127
```

브라우저 확인:

```text
http://127.0.0.1:5127/
```

## 16. GitHub

현재 원격 저장소:

```text
https://github.com/s5219071/saleserp.git
```

일반 작업 흐름:

```powershell
git status
git add .
git commit -m "describe change"
git push
```

주의:

- API key, JWT secret, DB password는 commit하지 않습니다.
- `appsettings.json`에는 placeholder만 둡니다.
- 실제 값은 환경변수나 배포 환경 secret manager에서 주입합니다.

## 17. 운영 배포 체크리스트

필수:

- `Jwt:SigningKey` 교체
- HTTPS 적용
- Cookie `SecurePolicy`를 `Always`로 변경
- Google Maps API key를 browser restricted key로 설정
- Google Cloud Console에서 Maps JavaScript API 활성화
- DB 저장소 적용
- ABR Lookup 실제 API 적용
- CSRF 대책 추가
- 로그와 audit trail 추가
- 관리자 계정 초기 비밀번호 변경

권장:

- 고객/게시글/그룹 삭제 audit
- Import 결과 다운로드
- 사용자 관리 화면
- refresh token 또는 session rotation
- 이미지 virus scan
- rate limiting
- structured logging
- health check endpoint

## 18. 자주 생기는 문제

### Google Map이 안 보임

확인:

- `GoogleMaps__ApiKey` 환경변수 존재 여부
- Maps JavaScript API 활성화 여부
- API key referrer 제한
- 브라우저 console의 `REQUEST_DENIED`

### XML Import 후 Customer에 안 보임

확인:

- Customer 화면의 Type filter 체크 여부
- XML 1행 header가 정확한지
- 필수값이 빠지지 않았는지
- Latitude, Longitude가 숫자인지

### 앱 재시작 후 데이터가 사라짐

정상 동작입니다. 현재 저장소는 in-memory입니다. 운영에서는 DB 저장소로 교체해야 합니다.

### 401 또는 403

확인:

- 로그인 상태
- `Authorization: Bearer {token}` 헤더
- 사용자 role
- 관리자 전용 API를 SALES 계정으로 호출했는지

## 19. 다음 개발 우선순위

1. DB 영속화 구현
2. 사용자 관리 화면 추가
3. Customer XML Import 결과 상세 리포트 추가
4. 운영용 ABR Lookup client 교체
5. CSRF 및 rate limiting 추가
6. 게시글 이미지 교체/삭제 세부 기능 추가
7. 테스트 프로젝트 추가
