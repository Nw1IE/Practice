using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Projects.models;
using Projects.StationLogger;

namespace Projects;

class Program
{
    private const int TotalCells = 20;
    private const int ChargerSlots = 4;
    private const int DiagnosticIter = 100000;
    private const int MinChargeMs = 300;
    private const int MaxChargeMs = 900;
    private const int MinCalibrationMs = 100;
    private const int MaxCalibrationMs = 300;
    private const int AutoCancelTimeoutMs = 0;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var logger = new global::Projects.StationLogger.StationLogger();

        logger.LogSystem("=== Инициализация станции испытания аккумуляторных ячеек ===");

        ThreadPool.GetAvailableThreads(out int workerThreadsStart, out int completionPortThreadsStart);
        logger.LogSystem($"Доступно потоков в пуле на старте: WorkerThreads = {workerThreadsStart}, CompletionPort = {completionPortThreadsStart}");

        using var semaphore = new SemaphoreSlim(ChargerSlots, ChargerSlots);
        using var mutex = new Mutex();
        using var cts = new CancellationTokenSource();
        using var countdown = new CountdownEvent(TotalCells);

        var results = new ConcurrentDictionary<int, ProcessingResult>();

        int completedCount = 0;
        int canceledCount = 0;
        int faultedCount = 0;

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            logger.LogSystem("Получен сигнал аварийной остановки (Ctrl+C)... Запрос отмены.");
            cts.Cancel();
        };

        if (AutoCancelTimeoutMs > 0)
        {
            cts.CancelAfter(AutoCancelTimeoutMs);
            logger.LogSystem($"Включена автоматическая отмена через {AutoCancelTimeoutMs} мс.");
        }

        var random = new Random();
        var cells = new Cell[TotalCells];
        for (int i = 0; i < TotalCells; i++)
        {
            cells[i] = new Cell
            {
                Id = i + 1,
                DiagnosticIterations = DiagnosticIter,
                ChargeDurationMs = random.Next(MinChargeMs, MaxChargeMs + 1),
                CalibrationDurationMs = random.Next(MinCalibrationMs, MaxCalibrationMs + 1)
            };
        }

        logger.LogSystem($"Партия из {TotalCells} ячеек успешно создана в памяти. Запуск обработки в ThreadPool...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var cell in cells)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ProcessCell(cell, semaphore, mutex, cts.Token, logger);
                }
                catch (Exception ex)
                {
                    cell.Status = CellStatus.Faulted;
                    cell.ErrorMessage = ex.Message;
                    logger.Log(cell.Id, cell.Status.ToString(), $"Критическая ошибка: {ex.Message}");
                }
                finally
                {
                    cell.EndTime = DateTime.Now;
                    results[cell.Id] = new ProcessingResult
                    {
                        CellId = cell.Id,
                        FinalStatus = cell.Status,
                        StartTime = cell.StartTime,
                        EndTime = cell.EndTime,
                        ErrorMessage = cell.ErrorMessage
                    };

                    switch (cell.Status)
                    {
                        case CellStatus.Completed:
                            Interlocked.Increment(ref completedCount);
                            break;
                        case CellStatus.Canceled:
                            Interlocked.Increment(ref canceledCount);
                            break;
                        case CellStatus.Faulted:
                            Interlocked.Increment(ref faultedCount);
                            break;
                    }

                    countdown.Signal();
                }
            });
        }

        bool finishedCleanly = await Task.Run(() =>
        {
            while (!countdown.IsSet)
            {
                if (cts.IsCancellationRequested)
                {
                    return countdown.Wait(TimeSpan.FromSeconds(10));
                }
                Thread.Sleep(50);
            }
            return true;
        });

        stopwatch.Stop();

        if (!finishedCleanly)
        {
            logger.LogSystem("ВНИМАНИЕ: Превышен лимит ожидания завершения задач после запроса отмены!");
        }

        logger.LogSystem("Обработка партии завершена. Вывод итоговой статистики...");

        ThreadPool.GetAvailableThreads(out int workerThreadsEnd, out int completionPortThreadsEnd);

        Console.WriteLine("\n==================================================");
        Console.WriteLine("               ИТОГОВАЯ СТАТИСТИКА                ");
        Console.WriteLine("==================================================");
        Console.WriteLine($" Общее количество ячеек              : {TotalCells}");
        Console.WriteLine($" Успешно завершено (Completed)       : {completedCount}");
        Console.WriteLine($" Отменено (Canceled)                 : {canceledCount}");
        Console.WriteLine($" Завершено с ошибкой (Faulted)       : {faultedCount}");
        Console.WriteLine($" Сумма статусов (проверка ТЗ)        : {completedCount + canceledCount + faultedCount}");
        Console.WriteLine($" Общее время выполнения партии       : {stopwatch.ElapsedMilliseconds} мс");
        Console.WriteLine($" Доступно потоков в пуле (Старт)     : {workerThreadsStart}");
        Console.WriteLine($" Доступно потоков в пуле (Конец)     : {workerThreadsEnd}");
        Console.WriteLine("==================================================\n");
    }

    private static void ProcessCell(Cell cell, SemaphoreSlim semaphore, Mutex mutex, CancellationToken cancellationToken, global::Projects.StationLogger.StationLogger logger)
    {
        cell.StartTime = DateTime.Now;
        logger.Log(cell.Id, "Queued", "Ячейка поставлена в очередь");

        if (cancellationToken.IsCancellationRequested)
        {
            cell.Status = CellStatus.Canceled;
            logger.Log(cell.Id, cell.Status.ToString(), "Отменена до начала обработки");
            return;
        }

        try
        {
            cell.Status = CellStatus.Diagnostics;
            logger.Log(cell.Id, cell.Status.ToString(), "Диагностика начата");

            double dummyCalc = 0;
            for (int i = 0; i < cell.DiagnosticIterations; i++)
            {
                dummyCalc += Math.Sqrt(i) * Math.Sin(i);

                if (i % 10000 == 0 && cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            logger.Log(cell.Id, "DiagnosticsDone", "Диагностика успешно завершена");

            cell.Status = CellStatus.WaitingForCharger;
            logger.Log(cell.Id, cell.Status.ToString(), "Ожидание доступного слота зарядки");

            bool semaphoreAcquired = false;
            try
            {
                semaphore.Wait(cancellationToken);
                semaphoreAcquired = true;
            }
            catch (OperationCanceledException)
            {
                cell.Status = CellStatus.Canceled;
                logger.Log(cell.Id, "ChargeWaitCanceled", "Ожидание слота зарядки прервано отменой");
                return;
            }

            try
            {
                cell.Status = CellStatus.Charging;
                logger.Log(cell.Id, cell.Status.ToString(), $"Зарядка начата (длительность: {cell.ChargeDurationMs} мс)");

                bool canceledDuringCharge = WaitHandle.WaitAny(new[] { cancellationToken.WaitHandle }, cell.ChargeDurationMs) == 0;

                if (canceledDuringCharge || cancellationToken.IsCancellationRequested)
                {
                    cell.Status = CellStatus.Canceled;
                    logger.Log(cell.Id, "ChargingInterrupted", "Зарядка прервана отменой");
                    return;
                }

                logger.Log(cell.Id, "ChargingDone", "Зарядка завершена");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    semaphore.Release();
                    logger.Log(cell.Id, "ChargerReleased", "Слот зарядки освобожден");
                }
            }

            cell.Status = CellStatus.WaitingForCalibration;
            logger.Log(cell.Id, cell.Status.ToString(), "Ожидание калибровочного стенда");

            bool mutexAcquired = false;
            try
            {
                int waitIndex = WaitHandle.WaitAny(new WaitHandle[] { mutex, cancellationToken.WaitHandle });

                if (waitIndex == 1 || cancellationToken.IsCancellationRequested)
                {
                    cell.Status = CellStatus.Canceled;
                    logger.Log(cell.Id, "CalibrationWaitCanceled", "Ожидание калибровочного стенда прервано отменой");

                    if (waitIndex == 0)
                    {
                        mutex.ReleaseMutex();
                    }
                    return;
                }

                mutexAcquired = true;

                if (cancellationToken.IsCancellationRequested)
                {
                    cell.Status = CellStatus.Canceled;
                    logger.Log(cell.Id, "CalibrationCanceled", "Отмена зафиксирована после получения стенда");
                    return;
                }

                cell.Status = CellStatus.Calibration;
                logger.Log(cell.Id, cell.Status.ToString(), $"Калибровка начата (длительность: {cell.CalibrationDurationMs} мс)");

                bool canceledDuringCalibration = WaitHandle.WaitAny(new[] { cancellationToken.WaitHandle }, cell.CalibrationDurationMs) == 0;

                if (canceledDuringCalibration || cancellationToken.IsCancellationRequested)
                {
                    cell.Status = CellStatus.Canceled;
                    logger.Log(cell.Id, "CalibrationInterrupted", "Калибровка прервана отменой");
                    return;
                }

                logger.Log(cell.Id, "CalibrationDone", "Калибровка успешно завершена");
            }
            finally
            {
                if (mutexAcquired)
                {
                    mutex.ReleaseMutex();
                    logger.Log(cell.Id, "CalibrationReleased", "Калибровочный стенд освобожден");
                }
            }

            cell.Status = CellStatus.Completed;
            logger.Log(cell.Id, cell.Status.ToString(), "Ячейка успешно прошла весь технологический цикл");
        }
        catch (OperationCanceledException)
        {
            cell.Status = CellStatus.Canceled;
            logger.Log(cell.Id, "Canceled", "Операция отменена");
        }
        catch (Exception ex)
        {
            cell.Status = CellStatus.Faulted;
            cell.ErrorMessage = ex.Message;
            logger.Log(cell.Id, "Faulted", $"Ошибка при обработке ячейки: {ex.Message}");
        }
    }
}