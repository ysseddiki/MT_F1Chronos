FROM docker.io/library/python:3.12-slim
WORKDIR /app

# VPS / réseau lent : le timeout pip par défaut (15 s) provoque des échecs
# « No matching distribution » alors que PyPI est simplement injoignable à temps.
ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DEFAULT_TIMEOUT=300

ARG PIP_INDEX_URL=
ARG PIP_TRUSTED_HOST=

COPY requirements.txt .
RUN python -m pip install --upgrade pip \
    && if [ -n "$PIP_INDEX_URL" ]; then \
         pip install --index-url "$PIP_INDEX_URL" \
           ${PIP_TRUSTED_HOST:+--trusted-host "$PIP_TRUSTED_HOST"} \
           --retries 10 --timeout 300 -r requirements.txt; \
       else \
         pip install --retries 10 --timeout 300 -r requirements.txt; \
       fi
COPY app ./app
ENV RESULTS_DATA=/data
ENV PYTHONUNBUFFERED=1
EXPOSE 8080
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8080", "--proxy-headers", "--forwarded-allow-ips", "*"]
