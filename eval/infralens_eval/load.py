"""Tiny load generator. Enough traffic to make faults visible; not a benchmark of the app."""

from __future__ import annotations

import json
import random
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass

RATES = {"none": 0.0, "light": 1.0, "steady": 10.0, "heavy": 40.0, "bad_requests": 1.0}


@dataclass
class LoadStats:
    sent: int = 0
    by_status: dict[int, int] | None = None

    def record(self, status: int) -> None:
        self.sent += 1
        self.by_status = self.by_status or {}
        self.by_status[status] = self.by_status.get(status, 0) + 1


class LoadGenerator:
    def __init__(self, base_url: str, profile: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.profile = profile
        self.stats = LoadStats()
        self._stop = threading.Event()
        self._threads: list[threading.Thread] = []

    def __enter__(self) -> LoadGenerator:
        rate = RATES[self.profile]
        if rate > 0:
            workers = max(1, int(rate // 5) + 1)
            for _ in range(workers):
                thread = threading.Thread(target=self._run, args=(rate / workers,), daemon=True)
                thread.start()
                self._threads.append(thread)
        if self.profile == "bad_requests":
            thread = threading.Thread(target=self._run_bad, args=(3.0,), daemon=True)
            thread.start()
            self._threads.append(thread)
        return self

    def __exit__(self, *exc: object) -> None:
        self._stop.set()
        for thread in self._threads:
            thread.join(timeout=5)

    def _request(self, method: str, path: str, body: dict | None = None) -> None:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(
            self.base_url + path, data=data, method=method, headers={"Content-Type": "application/json"}
        )
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                self.stats.record(response.status)
        except urllib.error.HTTPError as error:
            self.stats.record(error.code)
        except (urllib.error.URLError, TimeoutError):
            self.stats.record(0)

    def _run(self, rate: float) -> None:
        customers = [f"c-{n:03d}" for n in range(20)]
        while not self._stop.is_set():
            customer = random.choice(customers)
            roll = random.random()
            if roll < 0.6:
                self._request("POST", "/orders", {"customerId": customer, "items": [{"sku": "SKU-1", "quantity": 1}]})
            elif roll < 0.8:
                self._request("GET", f"/customers/{customer}/orders")
            else:
                self._request("GET", "/health")
            time.sleep(1.0 / rate)

    def _run_bad(self, rate: float) -> None:
        while not self._stop.is_set():
            self._request("POST", "/orders", {"customerId": "partner-feed", "items": []})
            time.sleep(1.0 / rate)
