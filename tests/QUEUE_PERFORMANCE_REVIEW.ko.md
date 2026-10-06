# PooledQueue 정적 성능 검토

상태: 2026-10-02의 정적 검토 기록이다. 이후 2026-10-06에 배치 입력·용량 기반 재사용·
대여 검사 경로 최적화를 적용했으므로 아래의 코드 위치와 미적용 제안은 당시 상태를 나타낸다.
현재 성능 비교 코드는 QueueBenchmarks의 `--comparison` 경로이며,
최신 계약 검사 결과는 [구현·검증 기록](QUEUE_VERIFICATION.ko.md)의 후속 검증을 참고한다.

검토일: 2026-10-02. 현재 소스와 기존 검증 기록만 읽었다. 이번 검토에서 빌드, 테스트, 벤치마크, 디버거 실행은 하지 않았으며 구현과 프로젝트 파일도 변경하지 않았다. 아래의 개선 효과는 새로 실측한 결과가 아니다.

## 판단

개선 여지는 있다. 직접 Enqueue/Dequeue의 우선 검토 대상은 **대여 검사 경로의 인라인과 예외 경로 분리**다. Enqueue의 조건부 용량 하한 갱신은 보조 후보이고, 기존 기록상 그 변경만으로 확실한 개선을 주장할 수 없다. 호출부의 Count + Dequeue를 TryDequeue로 바꾸는 것은 검사 횟수를 소스 수준에서 줄일 수 있지만, 개별 Dequeue 구현 자체를 빠르게 만드는 변경은 아니다.

현재 안정성 계약을 유지하면 원소마다 대여 유효성을 확인하는 비용은 남는다. 기존의 Queue 직접 호출 수준에 도달할지는 정적으로 판단할 수 없다. 큰 폭의 개선을 원하면 배치 API 또는 저장 구조 변경까지 검토해야 한다.

## 소스에서 확인한 비용

근거: `src/CrowRx.Pool/Collections/PooledQueue.cs`.

| 경로             | Queue 직접 호출에 추가되는 작업                                                                                         | 위치              |
|------------------|-------------------------------------------------------------------------------------------------------------------------|-------------------|
| Enqueue          | State/Validate 경로, null·Active·Generation 검사, 상태에서 Queue 참조 읽기, 추가 Count 읽기, CapacityFloor 비교 및 대입 | 28–35, 183–190행  |
| Dequeue          | State/Validate 경로, 동일한 수명 검사, 상태에서 Queue 참조 읽기                                                         | 28, 39, 183–190행 |
| Count 후 Dequeue | 두 API 각각의 수명 검사. 마지막 빈 상태의 Count 조회도 수행                                                             | 26, 39행          |

State → Validate → Queue라는 소스 호출 구조가 있다고 실제 기계어에서도 모든 호출이 남는 것은 아니다. 짧은 메서드는 자동으로 인라인될 수 있다. 따라서 'Validate가 인라인되지 않아 느리다'는 현재 확인된 사실이 아니라 확인할 가설이다.

직접 호출 경로에는 인터페이스 변환, 박싱, lock, delegate 생성이 없다. PooledQueue는 readonly struct이며 검사 상태는 공유 참조 객체에 있다. 이번 검토 범위에서 정상적인 Enqueue/Dequeue의 추가 GC 원인은 보이지 않는다. Queue의 배열 증설과 무효 대여의 예외 생성은 별도다.

기존 `tests/QUEUE_VERIFICATION.ko.md:175` 이후 기록에는 .NET 10 x64 Release에서 Enqueue 1.544 → 3.132ns, 고정 횟수 Dequeue 1.803 → 3.001ns, Count 조건부 Dequeue 1.720 → 4.259ns가 적혀 있다. 이번에 재측정하지 않았으며 Unity 런타임의 결과도 아니다. 진단용 구현은 다른 코드 배치와 호출 구조를 사용하므로 차이를 검사·필드 쓰기 각각의 정확한 비용으로 분해할 수 없다.

## 1. 우선 후보: 작은 검사 경로와 별도 throw helper

Validate는 정상 경로와 ObjectDisposedException 생성 경로를 함께 가진다. 예외 생성과 문자열 인수를 별도 메서드로 옮기고, 작은 검사 메서드에 AggressiveInlining을 검토할 수 있다. Enqueue/Dequeue 및 Count/State getter가 호출자까지 인라인될 가능성도 함께 고려해야 한다. 한 곳에 속성을 붙인다고 전체 경로의 인라인이 보장되지는 않는다.

검토용 형태이며 적용하지 않았다:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static QueueLeaseState<T> Validate(QueueLeaseState<T>? state, ulong generation)
{
    if (state is null || !state.Active || state.Generation != generation)
    {
        ThrowInvalidLease();
    }

    return state!;
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void ThrowInvalidLease()
{
    throw new ObjectDisposedException(nameof(PooledQueue<T>), "종료되었거나 초기화되지 않은 큐 대여입니다.");
}
```

이 형태는 기존 세 검사를 유지하고 정상 경로에 새 힙 할당을 추가하지 않는다. 예외 타입·메시지는 유지할 수 있지만 스택 트레이스에는 helper가 나타날 수 있다. null-forgiving 연산자는 런타임 검사를 제거하는 코드가 아니라 helper가 항상 throw한다는 사실을 nullable 분석에 보완하는 표현이다.

기대 효과는 정상 경로 코드 크기와 호출 비용을 줄일 가능성이다. 이미 인라인되는 런타임에서는 변화가 작거나 없을 수 있다. AggressiveInlining은 강제 보장이 아니며 불필요한 사용은 성능을 악화시킬 수도
있다. [Microsoft MethodImplOptions 문서](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.methodimploptions), [Microsoft의 정상 경로 분리 설명](https://devblogs.microsoft.com/premier-developer/a-common-execution-path-optimization/).

따라서 전체 API에 일괄 적용하는 것보다 Validate와 자주 쓰는 얇은 래퍼부터 검토하는 편이 적절하다. JIT와 Mono/IL2CPP의 최적화 결과를 동일하게 가정하지 않는다.

## 2. 보조 후보: CapacityFloor의 변경 없는 대입 줄이기

현재 Enqueue는 내부 Queue에 넣은 뒤 매번 다음 대입을 수행한다.

```csharp
state.CapacityFloor = Math.Max(state.CapacityFloor, state.Queue.Count);
```

예열된 용량 안에서 반복할 때 하한 값은 거의 바뀌지 않는다. Count가 하한을 넘을 때만 필드를 쓰는 형태는 기존 계약과 보수적인 용량 하한을 유지한다. Queue 참조와 Count를 지역 변수로 받으면 소스의 중복 참조 접근도 정리할 수 있다. 단, 컴파일러가 이미 같은 최적화를 할 수 있으므로 지역 변수만 추가해서 빨라진다고 주장할 수는 없다.

조건부 갱신도 Count 읽기와 비교는 남기며 분기를 추가한다. Math.Max가 실제 함수 호출로 남는다고 가정해서도 안 된다. 기존 기록의 Math.Max 경로 3.157ns와 조건부 경로 3.116ns는 산포가 겹쳤다 (`tests/QUEUE_VERIFICATION.ko.md:195`). **이 변경은 합리적인 실험 후보지만 확인된 성능 개선은 아니다.**

CapacityFloor 추적 자체를 지우면 안 된다. PrepareCapacity는 이 값으로 다음 Get (capacity)의 배열 교체 여부를 판단한다 (193–199행). 증설된 큐가 이미 충분한데 하한이 오래된 값이면 다음 대여에서 불필요하게 새 Queue/배열을 만들 수 있어 현재 만족하는 GC 특성이 달라진다. Dispose는 Queue.Clear를 먼저 하므로 반환 시 Count만 읽는 방식으로 최대 원소 수를 복원할 수도 없다 (81–83행).

## 3. 호출부 후보: TryDequeue로 검사 중복 줄이기

`while (queue.Count > 0) { ... queue.Dequeue() ... }`는 성공한 원소마다 PooledQueue의 수명 검사 경로에 두 번 들어간다. 기존 TryDequeue를 사용하는 루프는 원소당 한 번, 종료 시 실패 호출 한 번으로 줄어든다 (45행). FIFO와 원소 제거는 계속 내부 Queue가 담당한다.

이는 빈 큐에서 종료하는 루프에 적합하다. 고정 횟수로 꺼내며 빈 큐를 오류로 처리해야 하는 코드에서는 Dequeue의 예외 계약을 그대로 사용할 이유가 있다. 실제 개선 폭은 미확인이다. 이미 사용 중인 직접 Dequeue 한 번의 비용은 이 변경으로 줄어들지 않는다.

## 4. 큰 변경 후보: 배치 API 또는 상태 내부의 원형 버퍼

| 후보                                                            | 줄일 수 있는 비용                                                                                      | 조건과 비용                                                                            |
|-----------------------------------------------------------------|--------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|
| EnqueueRange(ReadOnlySpan<T>), Span<T> 대상으로 배출            | 배치당 수명 검사 한 번, 입력 배치당 하한 갱신 한 번                                                    | 소비자가 배치 처리를 사용할 수 있어야 함. 기존 단일 Enqueue/Dequeue에는 직접 효과 없음 |
| QueueLeaseState에 원형 배열·head/tail/count/version을 직접 보관 | 별도 Queue 객체로의 참조 접근, 별도 래퍼 경로, CapacityFloor 추적. 실제 배열 길이를 용량으로 사용 가능 | BCL Queue 기능과 계약을 다시 구현·검증해야 하는 큰 변경                                |

Span 배치 API는 사용자 콜백·사용자 IEnumerable 열거를 실행하지 않는 형태여야 한 번의 검사로 처리하기 쉽다. 현재 비스레드 안전·외부 직렬화 계약을 전제로 한다. 입력 중 배열 증설 예외로 일부 원소만 들어간 경우에도 하한 기록이 유지되도록 finally 등의 처리가 필요하다. 임의 콜백/열거자를 사용하는 배치에서 검사를 한 번만 하면 재진입 Dispose/재대여를 놓칠 수 있다.

원형 버퍼는 현재의 readonly 핸들과 공유 상태·세대 검사를 유지하는 방향으로 설계할 수 있다. 다만 참조를 포함한 T의 제거·Clear·Dispose 시 참조 정리, wrap-around, 성장 시 FIFO 복사, TrimExcess, 열거 중 변경 검사, 기본 Queue의 예외 및 복사 계약을 모두 보존해야 한다. 정적 분석만으로 이 변경을 검증했다고 볼 수 없다. 작은 튜닝을 시도하기 전에 시작할 변경은 아니다.

## 피해야 할 단순화

- Active만 제거: 세대는 Get에서 증가한다 (262행). Dispose 직후 재대여 전에는 세대가 같으므로 종료된 핸들이 다시 통과한다. Active를 없애려면 반환 시 무효화와 세대 최대값 폐기 정책까지 새로 설계해야 한다.
- Generation만 제거: 같은 상태가 재대여될 때 오래된 복사본의 접근을 막을 수 없다.
- 최초 한 번만 검사한 raw Queue를 소비자에게 노출: 이후 Dispose/재대여 검사를 우회한다.
- Enqueue의 하한 추적을 Dequeue로 단순 이동: Enqueue 비용을 Dequeue로 옮기고 TryDequeue/Clear/Dispose/TrimExcess 등의 모든 감소 경로를 함께 관리해야 한다. 두 연산을 같이 개선하려는 목표에는 우선순위가 낮다.
- 핸들에 Queue 참조 추가 캐시: 상태→Queue 접근을 줄일 후보지만 핸들이 커지고 복사 비용이 늘며 Queue 교체 수명도 함께 관리해야 한다. 정적 분석으로 이득을 보장하지 못한다.

## 권장 순서

1. Validate 예외 경로 분리와 작은 메서드의 인라인 후보 정리. 수명 검사는 전부 유지한다.
2. 빈 큐까지 배출하는 호출부에서 Count + Dequeue 대신 TryDequeue 검토.
3. Enqueue의 조건부 하한 갱신은 별도 후보로 취급. 기존 기록상 큰 개선 근거는 없다.
4. 단일 호출 튜닝이 부족하면 실제 소비자의 배치 가능성을 확인하고, 이후에만 원형 버퍼 재구현 검토.

이번 검토에서는 실행 검증을 하지 않았다. 위 후보의 우선순위는 소스 구조와 기존 기록에 근거한 판단이며 성능 향상 수치나 원인 확정은 아니다.
