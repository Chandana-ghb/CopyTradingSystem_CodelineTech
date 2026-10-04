import requests

url = 'https://myapi.fyers.in/docsv3'
headers = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8',
    'Accept-Language': 'en-US,en;q=0.5'
}
try:
    r = requests.get(url, headers=headers)
    print('Status:', r.status_code)
    with open('fyers_docsv3.html', 'w', encoding='utf-8') as f:
        f.write(r.text)
except Exception as e:
    print('Error:', e)
