# Index and Range expressions handling

## Index

| Parent           | Description                     |
| ---------------- | ------------------------------- |
| s[^2]            | *s.length - 2*                  |
| var i = ^3       | *new Index(3, true)*            |
| Index i; i = ^4  | *new Index(4, true)*            |
| s[1..^2]         | *1*, *s.length - 2*             |
| s[^7..^5]        | *s.length - 7*, *s.length - 5*  |

### Problems
