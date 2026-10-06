# QueuePool 구현·검증 기록

## 2026-10-06 후속 검증

최신 구현은 배열/List 배치 입력, IReadOnlyCollection의 Count 힌트, 조건부 용량 하한 갱신,
용량이 충분한 대기 큐의 우선 선택과 연결 목록, 대여 검사 인라인·예외 경로 분리를 포함한다.
CountInactive의 자동 속성 전환을 포함한 최신 사용자 수정 후에도 .NET 10.0.12에서 **354 checks PASS**와 예열 후 10,000회 반복 **0B**를 확인했다.
라이브러리와 계약 테스트의 Release netstandard2.1 빌드는 경고·오류가 없었다.
Unity Mono/IL2CPP에서의 실행과 성능은 검증하지 않았다.

이하 내용과 실행 명령은 **2026-10-02 당시 구현·환경의 기록**이다.
현재 테스트 프로젝트는 netstandard2.1이므로 실행에는 .NET 호스트의 runtimeconfig가 필요하며,
아래의 net10.0 출력 경로와 dotnet run 명령을 그대로 사용하면 안 된다.

## 2026-10-02 검증 기록

검증일: 2026-10-02 (Asia/Seoul). 변경 저장소는 `crowrx-pool` 하나다.
버전 변경, 패키지 게시, 커밋·푸시는 수행하지 않았다. QV 코드와 설치 DLL도 변경하지 않았다.

## 구현과 파일

- `src/CrowRx.Pool/Collections/PooledQueue.cs`: 세대 검사를 적용한 readonly struct 대여,
  내부 Queue 소유 객체, 구조체 열거자, 최근 반환 슬롯+Stack, 배열/List 입력 분기,
  용량 보장, Warmup, CountInactive, MaxInactive, 풀 Clear.
- `src/CrowRx.Pool/README.md`: Queue 상속 설명을 현재 계약에 맞게 수정.
- `src/CrowRx.Pool/QUEUE_POOL.ko.md`: 공개 수명·예외·복사·반환·스레드·할당·마이그레이션 계약.
- `src/CrowRx.Pool/CrowRx.Pool.csproj`: 마이그레이션 문서를 향후 패키지에 포함하도록 지정.
  netstandard2.1, C# 9, 기존 버전은 유지.
- `tests/CrowRx.Pool.QueueTests/{CrowRx.Pool.QueueTests.csproj,Program.cs}`: 실행 가능한 집중 계약 테스트.
- `tests/QueueBenchmarks/Program.cs`, `tests/QueueBenchmarks/{Current,Legacy}/*.csproj`:
  동일한 워크로드로 실제 두 DLL을 별도 프로세스에서 측정하는 재현 코드.
- `tests/QueueBenchmarks/QueueOperationDiagnostics.cs`: Enqueue·Dequeue 분리 측정과
  대여 검사/용량 추적을 구분하기 위한 진단 전용 구현. 아래 추가 분석 참조.
- `tests/.gitignore`: 검증 프로젝트의 bin/obj 제외.
- `tests/QUEUE_VERIFICATION.ko.md`: 이 기록.

List/Dictionary/HashSet/StringBuilder 및 Native/Unity 풀은 참조를 조사했으나 연결 변경이 필요하지 않았다.
Queue의 공개 반환형은 class에서 struct로 바뀌어 **기존 바이너리와 호환되지 않는다**.
Queue 타입 호환성, null·참조 동일성·class 제약에 의존한 코드는 마이그레이션해야 한다.
직접 using과 확인된 QV 세 BFS의 메서드 사용 형태는 유지된다. 실제 QV 재빌드는 하지 않았다.

## 사전 조사와 보존

11개 독립 저장소의 Git 상태를 확인했다. `crowrx`, `crowrx-json`, `crowrx-pool`,
`crowrx-trigger`, `crowrx-utility`, `crowrx-visualscripting`에 기존 변경 또는 미추적 파일이 있었다.
다른 저장소에는 쓰지 않았다. crowrx-pool의 기존 Unity Settings, Packages, ProjectSettings 변경과
미추적 .idea는 보존했다. 전체 diff의 ProjectSettings.asset 공백 오류는 기존 변경에 있던 것으로 수정하지 않았다.
이번 소스·프로젝트·문서 변경에 대한 별도 `git diff --check`는 통과했다.

조사한 workspace와 상위 경로에서 적용 가능한 AGENTS.md는 발견되지 않았다.
세션 안내에 있던 `.codex/AGENTS.md`도 실제 경로에는 없었다.
의미 기반 저장소 검색 도구는 현재 사용 가능한 도구 목록에 없었으므로 타입·메서드·역할별 검색과
실제 참조 검색을 수행했다. 광범위한 검색에서 Library, Temp, Logs, bin, obj를 제외했다.
특정 설치 패키지의 package.json을 확인할 때만 Library/PackageCache의 해당 패키지 경로를 읽었다.

요청의 이전 분석 파일 다섯 개는 모두 읽을 수 있었다. 이전 수치는 참고로만 사용하고 아래 결과는 새로 측정했다.
QV의 QueuePool 사용은 원래 제시된 세 줄과 일치했고 모두 함수 내부 동기 BFS였다.
QV는 NuGet CrowRx.Pool 1.0.4, Unity Git 패키지는 package.json 기준 com.crowlib.crowrx.pool 1.0.3이었다.
원본 Unity 프로젝트와 QV에 설치된 CrowRx.Pool 1.0.4 DLL의 SHA-256은 서로 같았다.
반환 후 접근 문제가 실제 QV에서 발생했다는 증거는 없다.

## 계약 검증

Release `netstandard2.1` 라이브러리 빌드: 경고 0, 오류 0.
대상 NETStandard.Library.Ref/2.1.0 API를 확인했고 TryPeek/TryDequeue/TrimExcess를 실제 대상으로 컴파일했다.
EnsureCapacity와 내부 배열 reflection은 구현에 사용하지 않았다.

Release `.NET 10.0.12` 테스트: **272 checks PASS**, 종료 코드 0.

- 대여·복사·반환·재대여, FIFO, wrap-around, 조회·배열 복사, 기본 Queue 인수 예외.
- 반환 후·재대여 후·default의 모든 컬렉션 API 및 명시적 인터페이스 접근에 ObjectDisposedException.
- 복사본, 박싱된 IDisposable, 중복·오래된 Dispose가 현재 대여와 대기 수를 변경하지 않음.
- 정상 직접/제네릭/비제네릭 순회, Reset, 순회 중 변경, 반환·재대여 이후 열거자 접근.
- 열거자 Dispose는 큐 대여를 반환하지 않고 대여 종료 후에도 정리용 호출이 가능함.
- 입력 GetEnumerator·MoveNext·Current·Dispose 실패 시 동일 예외 재전파, 빈 큐 반환.
- 반환 및 입력 실패 시 Queue가 원소 객체를 붙잡지 않음 (WeakReference와 GC 확인).
- SyncRoot가 실제 Queue를 노출하지 않음, ICollection<T>·Queue 타입 호환성이 없음.
- 상한 축소/0, 대기 수, Clear의 활성 대여 보호, Clear 이후 반환, Warmup의 활성 대여 보호.
- 작은 기존 큐와 TrimExcess 이후의 Get (4096)에서 4096개 채우기 중 배열 할당 없음.
- Warmup 직후 세 큐의 최초 동시 사용에서도 배열 준비 완료, 추가 할당 없음.
- 배열/List 입력, 직접 foreach, Reset, CopyTo, 네 동시 대여를 포함한 10,000회 반복: **0B**.
- 최대 세대 객체를 반환 시 폐기하여 세대 번호 순환 방지.
  이 테스트만 실제 최대 횟수의 반복 대신 테스트용 reflection으로 카운터를 진행시킨다. 배열은 조작하지 않는다.

Current의 잘못된 위치 동작은 실행 런타임의 Queue와 비교한다.
.NET 10은 순회 전후 Current에서 default를 반환할 수 있어 예외를 일률적으로 강제하지 않았다.
구현은 기본 열거자의 제네릭/비제네릭 Current와 Reset에 대여 검사를 더한다.
관련 실행 런타임 소스: [dotnet/runtime v10 Queue.cs](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Queue.cs).

Current benchmark 프로젝트 최초 restore에서 NuGet 취약점 조회의 TLS/접속 문제로 NU1900이 발생했다.
의존성 복원과 컴파일은 성공했다. 보안 감사 조회의 성공으로 해석하지 않는다.

## CPU·할당 실측

환경: Windows 10.0.26300, x64, .NET 10.0.12, SDK 10.0.401,
AMD64 Family 25 Model 33 Stepping 0 (AuthenticAMD). Release 별도 프로세스.
`DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0`.
각 워크로드 10,000회 예열, 7회 측정. 빈 대여 경로는 표본당 1,000,000회,
나머지는 표본당 100,000회다. 표의 시간은 ns/회 중앙값 [최소–최대]다.
표본 전 full GC는 측정 밖에서 수행한다. Action 호출, 검사, 원소 처리와 결과 sink 저장은 시간에 포함한다.
CPU affinity를 고정하지 않았고 다른 실행 중 앱도 종료하지 않았으므로 범위는 통계적 신뢰구간이 아니다.

| 워크로드                                      |     기존 DLL ns/회 [min–max] |     최종 DLL ns/회 [min–max] | 기존 → 최종 B/회 |
|-----------------------------------------------|-----------------------------:|-----------------------------:|-----------------:|
| 빈 Get+Dispose                                |       12.414 [11.995–12.566] |         9.642 [9.367–10.204] |            0 → 0 |
| 빈 Get+Count+Dispose                          |       15.227 [14.455–16.032] |       11.432 [10.868–12.116] |            0 → 0 |
| 직접 배열 입력/배출 256개                     |    775.817 [767.978–787.858] | 1040.613 [1008.137–1152.929] |            0 → 0 |
| IEnumerable 배열 입력/배출 256개              | 1393.093 [1381.381–1438.084] | 1338.271 [1326.921–1386.401] |           32 → 0 |
| IEnumerable List 입력/배출 256개              | 1912.978 [1899.847–1937.893] | 1418.888 [1390.153–1439.507] |           40 → 0 |
| 직접 입력/직접 foreach 256개                  |    806.104 [797.075–824.180] | 1167.659 [1147.740–1295.944] |            0 → 0 |
| 직접 입력/지역 인터페이스 foreach 256개       | 1549.380 [1539.806–1572.505] | 1133.888 [1118.257–1167.231] |          40 → 56 |
| 직접 입력/ToArray 256개                       |    405.272 [388.720–409.533] |    602.924 [595.224–628.516] |      1048 → 1048 |
| 사전 변환된 인터페이스의 foreach만 256개      | 1173.450 [1141.844–1200.460] | 1920.887 [1909.289–2010.659] |          40 → 56 |
| 직접 입력/강제 탈출 인터페이스 foreach 256개* | 1670.153 [1654.809–1701.802] | 2622.528 [2585.138–2697.088] |          40 → 88 |

*마지막 행은 `--boxing-only` 별도 실행에서 동일한 예열·표본 수로 측정했다.
NoInlining 경계를 넘어 인터페이스 핸들을 반환하여 지역 박싱 제거 최적화를 막는다.
최종 88B는 핸들 32B와 열거자 56B이며, 일반 지역 인터페이스 경로에서는 JIT가 핸들 박싱을 제거한 것으로 해석한다.
사전 변환 경로의 핸들 박싱은 측정 밖이며 매 열거자 56B만 포함한다.
비제네릭 열거의 값형 원소 박싱 비용은 이 표에서 측정하지 않았다.

안전성 검사를 적용한 최종 설계에서 직접 입력/배출은 약 **34%**, 직접 입력/순회는 약 **45%** 느려졌다.
배열/List 입력 열거자의 새 할당은 없어졌다. 빈 Get/Dispose의 개선은 단일 슬롯뿐 아니라
반환형·상태·풀 정책이 함께 바뀐 최종 구현의 비교이므로 캐시 슬롯 단독 효과로 주장하지 않는다.
동일 구조에서 검사만 껐다 켠 비교도 아니다. AggressiveInlining이나 작은 매개변수 변경으로 개선을 주장하지 않는다.
기존 참고 측정과 환경·예열 조건이 달라 그 숫자와 이번 표를 직접 연결하지 않는다.

초기 할당은 워밍업 반복과 분리한 한 번의 관찰이다:

| 초기 조건                                       | 기존 DLL | 최종 DLL |
|-------------------------------------------------|---------:|---------:|
| 해당 T 풀 첫 빈 대여·반환(정적 초기화 포함)     |     840B |     672B |
| 그 풀에서 처음 long 4096개 채움                 |  65,768B |  65,712B |
| 같은 큐 long 4096개 재사용                      |       0B |       0B |
| 풀 Clear 후 Get(4096) 생성·채움·반환            | API 없음 |  32,872B |
| 풀 Clear 후 Warmup(4, 4096), 큐·배열·Stack 포함 | API 없음 | 131,600B |

첫 T 사용에는 정적 초기화 비용이 포함되어 일반 Queue 하나의 크기와 같지 않다.
`GC.GetAllocatedBytesForCurrentThread`는 해당 스레드에서 만든 관리 힙의 새 할당량이다.
GC 정지 시간, 다른 스레드 할당, 전체 보관 메모리를 측정한 것이 아니다.
이 수치는 **Unity Editor·Mono·CoreCLR·IL2CPP·Player의 실측 결과가 아니다**.
QV 세 BFS는 Get (source)를 사용하지 않으므로 입력 열거 최적화가 그 BFS의 할당을 바로 줄이는 것은 아니다.
빈 대여 개선이나 여기의 CPU 증감을 전체 던전 생성 시간에 환산할 근거도 없다.

DLL SHA-256:

```text
기존 1.0.4: B2F2AB485ABDC386C5BE0E2123BD75CDE27390EEBB5CCF15B6D4C1936A25DE72
최종 측정: AE792CD7D842C8DB117C0650AB87DA2EA3C6D33F61A32703765060FD44C94434
```

## 재현

저장소 루트 (crowrx-pool)에서 실행한다. .NET 10 SDK가 필요하며 측정 프로젝트는 최종 netstandard2.1 DLL을 사용한다.
Legacy 프로젝트는 원본 Unity 검증 프로젝트에 설치되어 있는 기존 1.0.4 DLL을 읽기만 한다.
다른 설치본으로 비교할 때는 `-p:LegacyPoolPath=<절대 DLL 경로>`로 바꿀 수 있다.
프로젝트마다 별도 bin/obj를 사용하므로 같은 어셈블리 이름의 두 DLL을 섞지 않는다.

```powershell
dotnet build src/CrowRx.Pool/CrowRx.Pool.csproj -c Release -p:GeneratePackageOnBuild=false
dotnet run --project tests/CrowRx.Pool.QueueTests -c Release -p:GeneratePackageOnBuild=false
dotnet build tests/QueueBenchmarks/Current -c Release -p:GeneratePackageOnBuild=false
dotnet build tests/QueueBenchmarks/Legacy -c Release
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_ReadyToRun = '0'
dotnet tests/QueueBenchmarks/Legacy/bin/Release/net10.0/Legacy.dll
dotnet tests/QueueBenchmarks/Current/bin/Release/net10.0/Current.dll
```

측정 프로그램은 환경, 실제 로드한 DLL 경로·해시, 각 표본 시간과 할당량을 JSON 행으로 출력한다.
`--boxing-only`로 강제 핸들 탈출 경로만 따로 실행할 수 있다.

## Unity에서 아직 확인하지 않은 부분

원본의 기존 `src/CrowRx.Pool.Unity` 검증 프로젝트는 ProjectVersion 기준 6000.4.3f1이며
설치 DLL은 기존 1.0.4다. 해당 프로젝트에서 별도 Queue 계약 테스트는 발견하지 않았다.
연결 Editor 목록은 읽기 전용 Unity CLI로 확인했다. 샌드박스 내부의 빈 목록은 신뢰하지 않고
샌드박스 밖에서도 재확인했으며 연결된 프로젝트는 **qv-worktrees/worktree_01 / 6000.7.0b2** 하나였다.
원본 Pool 검증 프로젝트의 연결 Editor는 확인되지 않았다. QV Editor에는 명령을 실행하지 않았다.
Unity CLI 최신 버전 확인은 TLS 연결 실패로 완료하지 못했다.

이번 엔진 독립 변경은 실제 타깃 빌드와 일반 .NET 계약·할당·CPU 검증을 완료했다.
Unity 실행 검증, 원본 Unity 프로젝트에 최종 DLL을 넣은 재컴파일, Player 빌드,
Mono/CoreCLR/IL2CPP 및 실제 BFS 성능은 아직 검증하지 않았다.
소비자 패키지를 갱신할 때 해당 프로젝트에서 별도로 검증해야 한다.

## 추가 분석: Enqueue와 Dequeue의 비용 분리

2026-10-02, 위와 같은 .NET 10.0.12 x64 Release 환경에서 `--operations`로 측정했다.
원소 16,384개 × 256배치 = 표본당 4,194,304개 연산, 한 표본 예열 후 7개 표본을 수집했다.
CPU 타이밍에서 대여·반환, 배열 증설, Enqueue의 Clear, Dequeue의 사전 채우기를 제외했다.
배치별 타임스탬프 간격을 합산했다. 아래는 원소당 ns 중앙값 [min–max]다.
모든 표본에서 사전 준비를 포함한 추가 관리 힙 할당은 0B였다.

| 연산                               |            기존 DLL |            최종 DLL |
|------------------------------------|--------------------:|--------------------:|
| Enqueue                            | 1.544 [1.527–1.732] | 3.132 [3.119–3.238] |
| 고정 횟수 Dequeue, Count 조회 없음 | 1.803 [1.793–1.822] | 3.001 [2.959–3.060] |
| while(Count > 0)의 Dequeue         | 1.720 [1.710–1.863] | 4.259 [4.162–4.389] |

최종 Dequeue도 `State`를 통해 null·Active·Generation을 검사하고 내부 Queue를 찾아간다.
기존 Queue 상속 경로에는 이 대여 수명 검사가 없었다. 따라서 Dequeue의 기본 FIFO 구현은 그대로여도
공개 호출 전체의 CPU 비용은 늘어난다. 기존 256개 종합 측정에는 Count 검사까지 포함되어 있었다.

같은 프로세스에서 별도 진단 구조체를 만들어 Enqueue의 비용을 비교했다.
검사만 있는 진단 구현은 용량 하한을 추적하지 않으므로 제품의 전체 계약을 충족하는 대안이 아니다.
진단 구현은 실제 DLL의 검사 구조를 모사하지만 다른 JIT 코드 배치의 영향도 있어 비용을 정확히 가산·분해할 수 없다.

| 진단 Enqueue 경로                         | 원소당 ns [min–max] |
|-------------------------------------------|--------------------:|
| 일반 Queue 직접 접근                      | 1.597 [1.585–1.731] |
| 대여 검사, 용량 하한 추적 없음            | 2.646 [2.547–2.700] |
| 대여 검사 + Math.Max 용량 하한 추적       | 3.157 [3.056–3.403] |
| 대여 검사 + if(count > floor) 조건부 갱신 | 3.116 [3.100–3.403] |

이 결과는 대여 검사가 공통 비용이고, Enqueue에는 Count 조회·용량 하한 비교·저장 비용이 추가됨을 뒷받침한다.
약 0.5ns 차이는 Math.Max 함수 하나만의 비용으로 볼 수 없다. 조건부 갱신과 Math.Max 경로의
산포는 겹쳐 이번 결과로 if 변경의 확실한 개선을 주장할 수 없다.
일반 Queue의 고정 Dequeue는 같은 최종 프로세스에서 1.808ns [1.778–1.864]였다.

진단 측정은 처음 두 번 실행했으며 표는 전체 결과를 간결하게 다시 출력한 두 번째 실행이다.
첫 번째 실행에서도 기존 Dequeue는 약 1.85ns, 검사+Math.Max 진단 Dequeue는 약 2.99ns로
같은 경향을 보였다. affinity는 고정하지 않았으며 Unity 성능 결과는 아니다.
원본 라이브러리의 구현은 이 추가 분석에서 변경하지 않았다.

```powershell
dotnet build tests/QueueBenchmarks/Current -c Release -p:GeneratePackageOnBuild=false
dotnet build tests/QueueBenchmarks/Legacy -c Release
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_ReadyToRun = '0'
dotnet tests/QueueBenchmarks/Legacy/bin/Release/net10.0/Legacy.dll --operations
dotnet tests/QueueBenchmarks/Current/bin/Release/net10.0/Current.dll --operations
```