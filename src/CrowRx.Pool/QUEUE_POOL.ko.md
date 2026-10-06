# QueuePool 대여 계약과 마이그레이션

`PooledQueue<T>`는 내부 Queue를 소유한 객체의 한 번의 대여를 나타내는 `readonly struct`다.
다른 컬렉션 풀의 구현은 이번 변경에 포함되지 않는다. `netstandard2.1`과 엔진 독립성을 유지한다.

```csharp
using CrowRx.Pool.Collections;

QueuePool<int>.Warmup(inactiveCount: 4, capacity: 256);
using var queue = QueuePool<int>.Get(256);
queue.Enqueue(1);
foreach (int item in queue)
{
    // 직접 foreach의 핸들과 열거자는 박싱하지 않는다.
}
```

## 수명과 예외

- 모든 복사본은 같은 대여다. 소유권이 복사본마다 새로 생기지 않는다.
- 한 복사본의 `Dispose`는 대여를 종료하고 원소 참조를 정리한다. 다른 복사본도 즉시 무효다.
- 종료된 대여, 재대여 이전 세대의 핸들, `default` 핸들의 컬렉션 접근은 `ObjectDisposedException`이다.
  `Count`, 조회·복사, `TrimExcess`, 열거 생성 및 명시적 인터페이스 경로에도 같은 검사를 적용한다.
  무효한 대여에서는 잘못된 복사 인수보다 수명 검사가 우선한다.
- 중복 `Dispose`, 오래된 핸들·복사본의 `Dispose`, `default.Dispose()`는 무해하다.
  현재 다른 세대의 대여를 비우거나 다시 반환하지 않는다.
- 열거자의 `MoveNext`, 두 `Current`, `Reset`은 대여 세대를 검사한다. 열거 전에 만든 열거자도
  반환·재대여 후에는 무효다. `MoveNext`와 `Reset`은 기본 Queue의 변경 검출을 유지한다.
  유효한 대여에서 잘못된 위치의 `Current` 동작은 실행 런타임의 Queue 열거자 계약을 따른다.
- 열거자 `Dispose`는 열거만 정리한다. 큐 대여를 반환하지 않으며 대여 종료 후 정리 호출도 허용한다.
- 빈 큐의 `Peek`·`Dequeue`, 복사 인수 오류 등은 기본 Queue의 예외 계약을 따른다.
- 세대 번호가 최대값에 도달한 객체는 반환 시 폐기하여 번호 순환으로 이전 핸들이 부활하지 않게 한다.

`IEnumerable<T>`, `IEnumerable`, `IReadOnlyCollection<T>`, 비제네릭 `ICollection`을 구현한다.
Queue가 구현하지 않는 `ICollection<T>`는 추가하지 않는다. 내부 Queue를 반환하는 속성이나 변환은 없다.
`ICollection.SyncRoot`는 Queue가 아닌 내부 대여 객체 토큰이며 풀 전체의 동기화 잠금이 아니다.

## 용량, 보관, 할당

`Get()`은 초기 용량 4로 필요한 객체만 생성한다. 과거 네 개 객체 선생성은 제거했다.
반환 객체 하나는 별도 슬롯, 나머지는 대여 상태 안의 연결 목록에 보관한다. 같은 동시 대여 수와 원소 수로
예열하고 보관 상한 안에서 반복하면 정상적인 Get·처리·Dispose에 관리 힙 할당이 없다.

`Get(capacity)`는 최소 용량을 실제로 보장한다. 생성 용량과 실제 들어갔던 최대 원소 수를
보수적인 용량 하한으로 기록하며 Count가 기존 하한을 넘을 때만 갱신한다.
별도 슬롯의 하한이 충분하면 바로 대여한다. 부족하면 연결 목록에서 요청 용량을 보장하는
첫 대기 큐를 찾아 재사용하고, 그런 큐가 없을 때만 부족한 큐를 `new Queue<T>(capacity)`로 교체한다.
목록 탐색 비용은 대기 객체 수에 비례하므로 크기가 다른 요청에서 불필요한 생성과 맞바꾸는 비용이다.
MaxInactive가 큰 경우 이 탐색 비용도 고려해야 한다.
대상 참조 API에 없는 `EnsureCapacity`나 내부 배열 reflection은 사용하지 않는다.
`TrimExcess` 후 하한은 현재 Count로 낮춰 다음 용량 요청도 안전하게 보장한다.
실제 여유 배열 용량을 조회하지 못하므로 충분한 큐를 보수적으로 교체하는 경우는 있을 수 있다.
이 변경은 보장된 용량이 충분한 다른 대기 큐의 재사용을 개선하며, BCL이 자동 증설한 미확인 여유 용량을 추정하지 않는다.

`Warmup(inactiveCount, capacity)`는 기존 대기 큐의 용량을 준비하고 지정 수까지 채운다.
활성 대여는 변경하지 않는다. 생성·예열·증설에는 할당이 있다. 모든 대기 큐의 용량을 준비하므로
`inactiveCount`가 현재 대기 수보다 작거나 0이어도 기존 대기 큐는 용량 준비 대상이다.

`MaxInactive`는 T별 대기 객체 수 상한 (기본 32)이다. 0이면 반환 객체를 보관하지 않는다.
축소는 초과 대기 객체만 놓아주며 활성 대여에는 영향이 없다. `CountInactive`는 대기 수만 센다.
`Clear()`는 대기 객체를 놓아주고 활성 대여는 보호한다. Clear 이후 활성 대여의 반환은
현재 상한에 따라 다시 보관한다. 즉 Clear는 이전에 대여된 객체의 후속 반환을 차단하지 않는다.
대기 목록을 비울 때 연결도 끊으므로 오래된 핸들이 다른 대기 객체를 연결을 통해 붙잡지 않는다.

반환마다 `Queue.Clear`로 원소 참조를 정리하고 배열은 유지한다. 자동 Trim은 하지 않는다.
객체 수 상한은 배열 크기 또는 총 보관 메모리 상한이 아니다. 큰 용량을 계속 유지할지는
실제 재사용 수요를 기준으로 판단하고 필요하면 대여 시 명시적 Trim 또는 풀 Clear를 사용한다.

`Get(in IEnumerable<T>? source)`는 기존 호출 형태를 유지한다. null은 빈 대여다.
IEnumerable 변수 안에 담긴 배열과 구체 List도 실제 타입에 따라 직접 순회하여 입력 열거자 할당을 피한다.
다른 입력은 일반 인터페이스 순회다. 입력의 Count 조회는 대여 전에 수행한다.
대여 후 GetEnumerator·MoveNext·Current·열거자 Dispose에서 예외가 나면 원소를 정리하여 반환하고 예외를 다시 던진다.
`ReadOnlySpan<T>`는 대상에서 가능하지만 확인된 소비자 수요가 없어 이번 API에는 추가하지 않았다.

직접 `using var`와 구체 타입 `foreach`는 핸들·열거자를 박싱하지 않는다.
인터페이스 변환은 핸들을, 인터페이스 열거는 열거자를 박싱할 수 있다. JIT가 특정 지역 변환의
박싱을 제거할 수도 있으므로 모든 인터페이스 사용을 무할당으로 간주해서는 안 된다.
비제네릭 열거의 값형 원소도 박싱될 수 있다. `ToArray`의 비어 있지 않은 결과 배열 할당은 의도된 동작이다.

## 스레드 계약

풀과 대여는 스레드 안전하지 않다. 기존 풀도 동기화되지 않았으며 확인된 QV 소비자는 함수 안에서
using으로 동기 BFS를 수행한다. 같은 T에 대한 모든 풀 작업과 대여 접근을 하나의 외부 정책으로 직렬화해야 한다.
세대 검사와 `SyncRoot`만으로 검사와 사용 사이의 경쟁 조건을 막을 수 없다.

## 호환성 변경

class에서 struct로 변경하고 Queue 상속을 제거했으므로 **기존 바이너리와 호환되지 않는다**.
소비자와 의존 라이브러리를 새 DLL에 대해 다시 빌드해야 한다. 이번 작업에서는 버전·패키지를 게시하거나
소비자 설치본을 갱신하지 않는다.

- `using var queue = QueuePool<T>.Get()`와 명시적인 `using PooledQueue<T>`는 유지된다.
- `Queue<T>` 매개변수·필드·변환을 사용하던 코드는 `PooledQueue<T>`나 필요한 읽기 인터페이스로 바꿔야 한다.
- null 검사, 참조 동일성, class 제약, Queue 상속에 의존하는 코드는 수정해야 한다.
- 핸들을 다른 곳에 전달하면 같은 대여를 공유한다. 반환 책임자를 하나로 정하고 using 범위 밖으로 탈출시키지 않는다.
- `Get(default)`처럼 형식을 생략한 default 리터럴은 새 capacity 오버로드 때문에 모호할 수 있다.
  `Get()` 또는 형식을 지정한 source를 사용한다. 기존 `in IEnumerable<T>` 호출은 유지된다.

CrowRx 전체 실제 참조와 문서를 검색했고 별도 QueuePool 코드 소비자는 발견하지 않았다.
QV의 세 사용처는 `DungeonDirector.cs:456`, `BlueprintGenerator.cs:1196`, `BlueprintGenerator.cs:2219`로 재확인했다.
모두 Count·Enqueue·Dequeue를 사용하는 함수 내부 동기 BFS이며 Queue 타입으로 넘기는 사용은 발견하지 않았다.
반환 후 접근 문제가 실제 QV에서 발생했다는 증거는 없다. QV 코드와 설치 패키지는 변경하지 않았다.

검증 절차와 실측 결과는 원본 저장소의 `tests/QUEUE_VERIFICATION.ko.md`에 기록한다.