#pragma once
#include <atomic>
#include <memory>
#include <thread>
#include <utility>

namespace Promzona
{
    // The part of std::jthread the streaming workers use. The standard library of Xcode
    // still keeps std::jthread and std::stop_token behind an experimental switch.
    class StopToken
    {
        std::shared_ptr<const std::atomic<bool>> flag_;

    public:
        StopToken() = default;
        explicit StopToken(std::shared_ptr<const std::atomic<bool>> flag) : flag_(std::move(flag)) {}
        bool stop_requested() const noexcept
        {
            return flag_ && flag_->load(std::memory_order_acquire);
        }
    };

    // Runs `work(StopToken)` on its own thread; destruction asks it to stop and joins it.
    // A worker that sleeps on a condition variable must also be woken by its owner.
    class StopThread
    {
        std::shared_ptr<std::atomic<bool>> flag_;
        std::thread thread_;

    public:
        StopThread() = default;
        template <class Work>
        explicit StopThread(Work &&work) : flag_(std::make_shared<std::atomic<bool>>(false))
        {
            thread_ = std::thread([work = std::forward<Work>(work), token = StopToken(flag_)]() mutable { work(token); });
        }
        StopThread(StopThread &&) noexcept = default;
        StopThread &operator=(StopThread &&other) noexcept
        {
            if (this != &other)
            {
                stop_and_join();
                flag_ = std::move(other.flag_);
                thread_ = std::move(other.thread_);
            }
            return *this;
        }
        ~StopThread()
        {
            stop_and_join();
        }

        void request_stop() noexcept
        {
            if (flag_)
                flag_->store(true, std::memory_order_release);
        }
        bool joinable() const noexcept
        {
            return thread_.joinable();
        }
        void join()
        {
            if (thread_.joinable())
                thread_.join();
        }

    private:
        void stop_and_join()
        {
            request_stop();
            join();
        }
    };
}
